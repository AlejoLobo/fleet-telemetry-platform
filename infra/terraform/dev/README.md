# Entorno AWS de desarrollo (Docker Compose en EC2)

Despliegue **reproducible y económico** para desarrollo y demostración.
**No usar en producción.** No hay alta disponibilidad ni multi-AZ de datos.

Una instancia EC2 ejecuta el stack del repositorio con:

```bash
docker compose --profile app up -d --build
```

Servicios: Redpanda, TimescaleDB, API, Worker y Web.
Un Application Load Balancer expone Web (tráfico por defecto) y API (`/api/*`, `/health/*`).

## Diferencia con el blueprint

| Ruta | Rol |
|------|-----|
| `infra/terraform/` | Blueprint conceptual (VPC/RDS/ECS/ECR); no despliega el pipeline Timescale |
| `infra/terraform/dev/` | Entorno ejecutable en una EC2 + Compose + ALB |

## Prerrequisitos

- Terraform `>= 1.9.8`
- Credenciales AWS con permisos para VPC, EC2, ELB, IAM, Secrets Manager
- AMI Amazon Linux 2023 x86_64 de la región (`ami_id`)
- Repositorio Git accesible por HTTPS desde la instancia

## Variables obligatorias

| Variable | Descripción |
|----------|-------------|
| `ami_id` | AMI Amazon Linux 2023 de la región |
| `instance_type` | Tipo EC2 (p. ej. `t3.large`) |
| `allowed_cidr_blocks` | CIDRs con acceso HTTP al ALB |
| `app_git_repository` | URL HTTPS del repositorio |
| `app_git_ref` | SHA Git completo de **40** caracteres hexadecimales |

Credenciales PostgreSQL/TimescaleDB: las genera Terraform (`random_password`),
las guarda en Secrets Manager y la instancia las lee al arrancar con su IAM role.
No se escriben en `user_data` ni en archivos Terraform versionados.

## AMI (`ami_id`)

```bash
aws ec2 describe-images --owners amazon \
  --filters "Name=name,Values=al2023-ami-2023*-x86_64" "Name=state,Values=available" \
  --query 'sort_by(Images,&CreationDate)[-1].ImageId' --output text
```

## `app_git_ref` (SHA completo)

```bash
git rev-parse HEAD
```

No usar `latest` ni nombres de rama flotantes.

## Uso

```bash
cd infra/terraform/dev
cp terraform.tfvars.example terraform.tfvars
# editar ami_id, instance_type, allowed_cidr_blocks, app_git_repository, app_git_ref

terraform init
terraform plan
terraform apply
```

Outputs no sensibles: `alb_url`, `instance_id`, `vpc_id`, `public_subnet_ids`, `secret_arn`, `app_git_ref`.

## Acceso con SSM (sin SSH)

```bash
aws ssm start-session --target "$(terraform output -raw instance_id)"
sudo tail -n 200 /var/log/fleet-telemetry-user-data.log
sudo tail -n 200 /var/log/cloud-init-output.log
```

## Validar Web y API

```bash
ALB="$(terraform output -raw alb_url)"
curl -fsS "$ALB/" | head
curl -fsS "$ALB/health/ready"
curl -fsS "$ALB/health/live"
```

## Destroy

```bash
terraform destroy
```

## Recursos con costo

Cobran: EC2, EBS gp3 cifrado, ALB, direcciones IPv4 públicas, Secrets Manager y tráfico de datos.
No hay NAT Gateway, MSK, EKS ni RDS gestionado en este stack.

## Limitaciones conscientes

- Una sola EC2: sin alta disponibilidad ni multi-AZ de datos
- Datos de TimescaleDB/Redpanda en el disco de la instancia
- HTTP sin TLS (sin Route53 ni certificados en este FT)
- Uso exclusivo para desarrollo y demostración
