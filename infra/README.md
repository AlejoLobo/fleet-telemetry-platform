# Infraestructura AWS

Índice del monorepo: [../docs/README.md](../docs/README.md).

Hay **dos** capas Terraform separadas a propósito:

## 1. Blueprint conceptual — `infra/terraform/`

Mapa de referencia (VPC, subnets, RDS PostgreSQL 16, ECS cluster, ECR, logs).

| Incluye | No incluye |
|---------|------------|
| VPC + IGW | TimescaleDB real |
| Subnets públicas/privadas | MSK / Kafka gestionado |
| RDS PostgreSQL 16 (sin hypertables) | ALB + ECS services |
| ECS cluster (sin services) | Despliegue Web |
| ECR api/worker | Alta disponibilidad / autoscaling |

Sirve para conversar arquitectura en sustentación. **No** es un despliegue completo del MVP.
RDS del blueprint **no** sustituye TimescaleDB.

Uso:

```bash
cd infra/terraform
export TF_VAR_db_password="cambiar-en-local"
terraform init
terraform plan
```

## 2. Entorno ejecutable de desarrollo — `infra/terraform/dev/`

Ruta reproducible y económica para **desarrollo/demostración** (no producción):

- VPC + 2 subnets públicas + Internet Gateway
- 1× EC2 con Docker Compose (`--profile app`)
- Redpanda + TimescaleDB + API + Worker + Web
- Application Load Balancer (HTTP 80)
- Secrets Manager + IAM (lectura del secreto) + SSM (sin SSH público)

Documentación operativa: [`terraform/dev/README.md`](terraform/dev/README.md).

### Prerrequisitos

- Terraform `>= 1.9.8`
- Credenciales AWS (VPC, EC2, ELB, IAM, Secrets Manager)
- `ami_id` de Amazon Linux 2023 en la región
- `app_git_ref` = SHA Git completo de 40 caracteres hexadecimales (obligatorio).
  Debe apuntar a un commit que **ya contenga FT-009** (normalmente el merge commit
  de FT-009 en `develop`). El valor de ejemplo en `terraform.tfvars.example` es
  inválido a propósito y debe reemplazarse antes de `terraform plan`; Terraform
  lo rechaza hasta que cumpla `^[0-9a-f]{40}$`.

### Despliegue

```bash
cd infra/terraform/dev
cp terraform.tfvars.example terraform.tfvars
# ami_id, instance_type, allowed_cidr_blocks, app_git_repository, app_git_ref
# (reemplazar PEGAR_SHA_MERGE_FT009_DE_40_CARACTERES por el SHA real de FT-009)
terraform init
terraform plan
terraform apply
```

Obtener AMI:

```bash
aws ec2 describe-images --owners amazon \
  --filters "Name=name,Values=al2023-ami-2023*-x86_64" "Name=state,Values=available" \
  --query 'sort_by(Images,&CreationDate)[-1].ImageId' --output text
```

Fijar el SHA a desplegar (tras fusionar FT-009 en `develop`):

```bash
git fetch origin develop
git rev-parse origin/develop
```

### Acceso SSM y validación

```bash
aws ssm start-session --target "$(terraform output -raw instance_id)"
# logs: /var/log/fleet-telemetry-user-data.log

ALB="$(terraform output -raw alb_url)"
curl -fsS "$ALB/"
curl -fsS "$ALB/health/ready"
```

### Destruir

```bash
terraform destroy
```

### Seguridad del entorno `dev`

| Superficie | Exposición |
|------------|-----------|
| ALB :80 | Solo `allowed_cidr_blocks` |
| EC2 :3000 / :5000 | Solo desde el SG del ALB |
| SSH | No expuesto |
| 5432 / 19092 | No expuestos al ALB ni a Internet (SG) |
| Secreto Postgres | Secrets Manager; recuperado en arranque por IAM role |

### Costos y limitaciones

Cobran EC2, EBS, ALB, IPv4 públicos, Secrets Manager y tráfico.
Una sola EC2: datos locales, sin multi-AZ ni HA.
HTTP sin TLS / Route53 / WAF / CloudFront.
**Solo desarrollo y demostración.**

## Persistencia de series de tiempo en AWS

| Opción | Notas |
|--------|-------|
| Entorno `dev/` (Compose + Timescale) | Demo/dev funcional |
| [Timescale Cloud](https://www.timescale.com/cloud) | Opción gestionada productiva |
| TimescaleDB self-hosted | Operación propia |
