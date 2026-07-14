# Terraform — detalle operativo

Ver el overview en [`../README.md`](../README.md).

## Blueprint conceptual (`infra/terraform/`)

```bash
cd infra/terraform
export TF_VAR_db_password="cambiar-en-local"

terraform init
terraform plan
terraform validate
```

Variables sensibles: `TF_VAR_db_password` (RDS del blueprint; **no** es TimescaleDB).

Outputs: `vpc_id`, `public_subnet_ids`, `private_subnet_ids`, `ecs_cluster_name`, `db_endpoint`, ECR URLs.

No incluye task definitions de ECS ni un despliegue aparente del pipeline completo;
el entorno ejecutable está en `dev/`.

## Entorno ejecutable (`infra/terraform/dev/`)

Ver [`dev/README.md`](dev/README.md).
