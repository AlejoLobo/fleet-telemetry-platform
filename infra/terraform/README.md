# Terraform AWS — Fleet Telemetry (Fase 6)

Blueprint de infraestructura para desplegar la plataforma en AWS.

## Recursos

- VPC con subnets públicas y privadas
- RDS PostgreSQL 16 (compatible con extensión TimescaleDB en despliegue manual)
- ECS Cluster (contenedores API/Worker)
- Security groups para API y base de datos

## Uso

```bash
cd infra/terraform
export TF_VAR_db_password="cambiar-en-produccion"

terraform init
terraform plan
terraform apply
```

## Notas

- Para TimescaleDB en AWS, habilitar la extensión tras crear la instancia o usar Timescale Cloud.
- Kafka/Redpanda: usar MSK o un cluster gestionado aparte; no incluido en este blueprint mínimo.
- Complementar con task definitions ECS para `backend/Dockerfile` y `backend/Dockerfile.worker`.
