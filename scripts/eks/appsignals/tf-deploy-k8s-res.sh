#!/bin/bash

CLUSTER_NAME=${1:-"petclinic-demo"}
REGION=${2:-"us-west-2"}
NAMESPACE=${3:-"default"}
OPERATION=${4:-"apply"}
ACCOUNT_ID=`aws sts get-caller-identity | jq .Account -r`

# change the directory to the script location so that the relative path can work
cd "$(dirname "$0")"

host="petclinic-database.cufgmmyvvbb2.us-west-2.rds.amazonaws.com"
port="5432"

for config in $(ls ./sample-app/*.yaml)
do
    sed -e "s/111122223333.dkr.ecr.us-west-2/$ACCOUNT_ID.dkr.ecr.$REGION/g" -e 's#\${REGION}'"#${REGION}#g" -e 's#\${DB_SERVICE_HOST}'"#${host}#g" $config | kubectl -v=2 ${OPERATION} --namespace=$NAMESPACE -f -
done

sleep 60s

# Save the endpoint URL to a variable
endpoint=$(kubectl get ingress -o json  --output jsonpath='{.items[0].status.loadBalancer.ingress[0].hostname}')

# Force rollout restart of all deployments to ensure they pick up the latest images
echo "Forcing rollout restart of all deployments to ensure they use the latest images"
kubectl rollout restart deployment --namespace=$NAMESPACE