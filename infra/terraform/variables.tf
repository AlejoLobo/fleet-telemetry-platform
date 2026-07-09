variable "aws_region" {
  description = "Región AWS"
  type        = string
  default     = "us-east-1"
}

variable "project_name" {
  description = "Prefijo de recursos"
  type        = string
  default     = "fleet-telemetry"
}

variable "environment" {
  description = "Entorno (dev, staging, prod)"
  type        = string
  default     = "dev"
}

variable "db_username" {
  description = "Usuario maestro RDS PostgreSQL"
  type        = string
  default     = "fleet"
}

variable "db_password" {
  description = "Contraseña RDS (usar TF_VAR_db_password o secrets)"
  type        = string
  sensitive   = true
}

variable "db_instance_class" {
  description = "Clase de instancia RDS"
  type        = string
  default     = "db.t4g.micro"
}
