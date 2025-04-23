#!/usr/bin/env bash
export REPOSITORY_PREFIX=${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com

aws ecr get-login-password --region ${REGION} | docker login --username AWS --password-stdin ${REPOSITORY_PREFIX}

aws ecr create-repository --repository-name dotnet-petclinic-payment --region ${REGION} --no-cli-pager || true
docker build -t dotnet-petclinic-payment ./dotnet-petclinic-payment/PetClinic.PaymentService --no-cache
docker tag dotnet-petclinic-payment:latest ${REPOSITORY_PREFIX}/dotnet-petclinic-payment:latest
docker tag dotnet-petclinic-payment:latest ${REPOSITORY_PREFIX}/dotnet-petclinic-payment:${COMMIT_SHA}
docker push ${REPOSITORY_PREFIX}/dotnet-petclinic-payment:latest
docker push ${REPOSITORY_PREFIX}/dotnet-petclinic-payment:${COMMIT_SHA}