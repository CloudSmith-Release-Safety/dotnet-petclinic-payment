using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PetClinic.PaymentService;
using Steeltoe.Discovery.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true)
    .AddEnvironmentVariables();

builder.SetEurekaIps();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// BREAKING CHANGE: Add JWT Authentication (v7.0.0)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"] ?? "default-secret-key"))
        };
        
        // BREAKING: New authentication events (requires client updates)
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"error\":\"authentication_failed\",\"message\":\"Invalid or expired token\"}");
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("{\"error\":\"unauthorized\",\"message\":\"Bearer token required\"}");
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddSingleton<IDynamoDBContext, DynamoDBContext>();
builder.Services.AddSingleton<IPetClinicContext, PetClinicContext>();

builder.Services.AddDiscoveryClient();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// BREAKING CHANGE: Authentication middleware must be added before authorization
app.UseAuthentication();
app.UseAuthorization();

app.UseHealthChecks("/healthz");

// BREAKING CHANGE: All payment endpoints now require authentication
app.MapGet("/owners/{ownerId:int}/pets/{petId:int}/payments",
    [Authorize] async (
        int ownerId,
        int petId,
        [FromServices] IPetClinicContext context) =>
    {
        var petResponse = await context.HttpClient.GetAsync($"http://customers-service/owners/{ownerId}/pets/{petId}");

        if (!petResponse.IsSuccessStatusCode)
        {
            return Results.BadRequest();
        }

        var request = context.DynamoDbContext.ScanAsync<Payment>([]);

        var payments = new List<Payment>();

        do
        {
            var page = await request.GetNextSetAsync();
            payments.AddRange(page);
        } while (!request.IsDone);

        return Results.Ok(payments.Where(x => x.PetId == petId).ToList());
    }
);

app.MapGet("/owners/{ownerId:int}/pets/{petId:int}/payments/{paymentId}",
    async (
        int ownerId,
        int petId,
        string paymentId,
        [FromServices] IPetClinicContext context) =>
    {
        try
        {
            var petResponse = await context.HttpClient.GetAsync($"http://customers-service/owners/{ownerId}/pets/{petId}");
        }
        catch (HttpRequestException ex)
        {
            // check exception message, if it contains "Connection refused" then return ServiceUnavailable
            if (ex.Message.Contains("Pet not found, petId: "))
            {
                return Results.StatusCode((int)HttpStatusCode.BadRequest);
            }

            return Results.StatusCode((int)HttpStatusCode.ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.StatusCode((int)HttpStatusCode.InternalServerError);
        }

        if (!petResponse.IsSuccessStatusCode)
        {
            return Results.BadRequest();
        }

        var payment = await context.DynamoDbContext.LoadAsync<Payment>(paymentId);

        return payment == null ? Results.NotFound() : Results.Ok(payment);
    }
);

app.MapPost("/owners/{ownerId:int}/pets/{petId:int}/payments/",
    async (
        int ownerId,
        int petId,
        Payment payment,
       [FromServices] IPetClinicContext context) =>
    {
        var petResponse = await context.HttpClient.GetAsync($"http://customers-service/owners/{ownerId}/pets/{petId}");

        if (!petResponse.IsSuccessStatusCode)
        {
            return Results.BadRequest();
        }

        payment.PetId = petId;
        payment.Id ??= Guid.NewGuid().ToString();

        await context.DynamoDbContext.SaveAsync(payment);

        return Results.Ok(payment);
    }
);

app.MapDelete("/clean-db", async ([FromServices] IPetClinicContext context) =>
{
    await context.CleanDB();

    return Results.Ok();
});

InitializeDB();

await app.RunAsync();

async void InitializeDB()
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<IPetClinicContext>();
    await context.InitializeDB();
}