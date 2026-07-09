# Terraform AWS — Fleet Telemetry

Blueprint de infraestructura para desplegar la plataforma en AWS (no es despliegue productivo completo).

## Recursos incluidos

- VPC con subnets públicas y privadas
- RDS PostgreSQL 16 (compatible con extensión TimescaleDB en despliegue manual)
- ECS Cluster Fargate
- ECR repositories: `fleet-telemetry-api`, `fleet-telemetry-worker`
- ECS task definitions (API + Worker) — blueprint sin service/ALB
- Security groups para API y base de datos
- CloudWatch log group

## Uso

```bash
cd infra/terraform
export TF_VAR_db_password="cambiar-en-produccion"

terraform init
terraform plan
terraform validate
terraform apply
```

## Variables relevantes

| Variable | Descripción |
|----------|-------------|
| `TF_VAR_db_password` | Contraseña RDS (obligatoria) |
| `container_image_api` | URI ECR de la API (opcional) |
| `container_image_worker` | URI ECR del Worker (opcional) |

## Outputs

- `rds_endpoint`, `vpc_id`, `ecs_cluster_name`
- `ecr_api_repository_url`, `ecr_worker_repository_url`
- `ecs_task_definition_api_arn`, `ecs_task_definition_worker_arn`

## Qué falta para producción

- MSK o Kafka gestionado (sustituir `REPLACE_WITH_MSK_OR_KAFKA_ENDPOINT` en task definitions)
- ALB + ECS services
- Secrets Manager para JWT y connection strings
- TimescaleDB: habilitar extensión en RDS o usar Timescale Cloud
