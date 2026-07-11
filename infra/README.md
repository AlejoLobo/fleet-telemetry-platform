# Infraestructura AWS — blueprint ejecutivo

Índice de documentación del monorepo: [../docs/README.md](../docs/README.md).

Terraform en `infra/terraform/` describe la **base** de red y cómputo para Fleet Telemetry. Es un **blueprint**, no un despliegue productivo completo.

## Qué incluye

| Recurso | Rol |
|---------|-----|
| VPC + subnets públicas/privadas | Red base |
| Internet Gateway + route table pública | Salida a Internet desde subnets públicas |
| Security groups API / datos | HTTP 80 y PostgreSQL 5432 (solo desde SG API) |
| RDS PostgreSQL 16 | **Solo PostgreSQL estándar** — AWS RDS no ofrece TimescaleDB como extensión administrada |
| ECS cluster Fargate | Contenedores API/Worker (sin services aún) |
| ECR (`api`, `worker`) | Imágenes Docker |
| Task definitions de ejemplo | Plantilla; placeholders de Kafka/secrets |
| CloudWatch log group | Logs de API |

## Outputs útiles

Tras `terraform apply`:

| Output | Uso |
|--------|-----|
| `vpc_id` | Referencia de red |
| `public_subnet_ids` | Futuro ALB / ingress |
| `private_subnet_ids` | RDS y tasks Fargate |
| `ecs_cluster_name` | Cluster ECS |
| `db_endpoint` | Endpoint RDS PostgreSQL (no TimescaleDB) |

## Persistencia de series de tiempo en AWS

El blueprint crea **RDS PostgreSQL 16** como placeholder de red/seguridad. **No es TimescaleDB** y no soporta `CREATE EXTENSION timescaledb` en RDS estándar.

Opciones honestas para producción:

| Opción | Cuándo usarla | Notas |
|--------|---------------|-------|
| [Timescale Cloud](https://www.timescale.com/cloud) | Recomendado para MVP productivo | Servicio gestionado, hypertables nativas, connection string en Secrets Manager |
| TimescaleDB self-hosted (EC2/ECS/EKS) | Control total del motor | Imagen `timescale/timescaledb-ha` o paquetes oficiales; operación propia |
| RDS PostgreSQL (este blueprint) | Solo demostrar red/IAM/ECS | Válido para tablas relacionales sin hypertables; **no** para el pipeline actual sin cambios |

Las task definitions usan `TimescaleDb__ConnectionString` como nombre de configuración de la app; el valor debe apuntar al endpoint Timescale real, no al RDS del blueprint si se esperan hypertables.

También: URLs ECR y ARNs de task definitions de ejemplo.

## Variables sensibles

| Variable | Sensitive | Cómo pasarla |
|----------|-----------|--------------|
| `db_password` | `true` | `export TF_VAR_db_password="..."` (nunca en git) |

## Uso

```bash
cd infra/terraform
export TF_VAR_db_password="cambiar-en-produccion"

terraform init
terraform plan
terraform validate
terraform apply
```

## Limitaciones (consciente — no es producción)

- **No MSK / Kafka gestionado.** El pipeline local usa Redpanda; en AWS habría que añadir MSK (u otro broker) y cablear bootstrap en las tasks.
- **No ALB completo.** No hay Application Load Balancer, listeners ni target groups; tampoco ECS services que registren tareas.
- **No task definitions productivas.** Las definitions en `ecs_tasks.tf` son plantilla (CPU/memoria mínimos, placeholders de Kafka/secrets). No sustituyen un despliegue con Secrets Manager, health checks y autoscaling.
- **No dashboard deploy.** El frontend Next.js (`web/`) no tiene hosting (CloudFront/S3/ECS) en este blueprint.

Para demo local seguir usando Docker Compose. Este Terraform sirve para explicar el mapa de infraestructura en sustentación o como punto de partida.

Detalle adicional de comandos: `infra/terraform/README.md`.
