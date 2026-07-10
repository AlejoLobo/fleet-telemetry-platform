# Terraform — detalle operativo

Ver el overview ejecutivo y limitaciones en [`../README.md`](../README.md).

## Comandos

```bash
cd infra/terraform
export TF_VAR_db_password="cambiar-en-produccion"

terraform init
terraform plan
terraform validate
terraform apply
```

## Variables

| Variable | Descripción |
|----------|-------------|
| `TF_VAR_db_password` | Contraseña RDS (`sensitive = true`, obligatoria) |
| `container_image_api` | URI ECR de la API (opcional; ver `ecs_tasks.tf`) |
| `container_image_worker` | URI ECR del Worker (opcional) |

## Outputs principales

`vpc_id`, `public_subnet_ids`, `private_subnet_ids`, `ecs_cluster_name`, `db_endpoint`
