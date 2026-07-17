variable "aws_region" {
  description = "Región AWS del entorno de desarrollo"
  type        = string
  default     = "us-east-1"
}

variable "project_name" {
  description = "Prefijo de nombres de recursos"
  type        = string
  default     = "fleet-telemetry"
}

variable "ami_id" {
  description = "AMI Amazon Linux 2023 (x86_64) de la región. Ver README para obtenerla."
  type        = string

  validation {
    condition     = can(regex("^ami-[0-9a-f]{8,17}$", var.ami_id))
    error_message = "ami_id debe ser un identificador AMI válido (ami-...)."
  }
}

variable "instance_type" {
  description = "Tipo de instancia EC2 (obligatorio; p. ej. t3.large para Redpanda + Timescale + API + Worker + Web)"
  type        = string
}

variable "root_volume_size_gb" {
  description = "Tamaño del disco raíz cifrado (GiB)"
  type        = number
  default     = 40
}

variable "allowed_cidr_blocks" {
  description = "CIDRs autorizados a acceder al ALB por HTTP (puerto 80)"
  type        = list(string)

  validation {
    condition     = length(var.allowed_cidr_blocks) > 0
    error_message = "allowed_cidr_blocks no puede estar vacío."
  }
}

variable "app_git_repository" {
  description = "URL HTTPS del repositorio Git a clonar en la instancia"
  type        = string

  validation {
    condition     = can(regex("^https://", var.app_git_repository))
    error_message = "app_git_repository debe ser una URL HTTPS."
  }
}

variable "app_git_ref" {
  description = "SHA completo de 40 caracteres hexadecimales a desplegar (no usar ramas ni latest)"
  type        = string

  validation {
    condition     = can(regex("^[0-9a-f]{40}$", var.app_git_ref))
    error_message = "app_git_ref debe ser un SHA Git completo de 40 caracteres hexadecimales en minúsculas."
  }
}

variable "docker_compose_version" {
  description = "Versión fijada de Docker Compose (plugin CLI) a instalar en la instancia"
  type        = string
  default     = "2.29.7"
}
