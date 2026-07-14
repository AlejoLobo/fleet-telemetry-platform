output "alb_url" {
  description = "URL HTTP del Application Load Balancer"
  value       = "http://${aws_lb.dev.dns_name}"
}

output "instance_id" {
  description = "ID de la instancia EC2 que ejecuta Docker Compose"
  value       = aws_instance.compose.id
}

output "vpc_id" {
  description = "ID de la VPC del entorno de desarrollo"
  value       = aws_vpc.dev.id
}

output "public_subnet_ids" {
  description = "IDs de las subnets públicas"
  value       = aws_subnet.public[*].id
}

output "secret_arn" {
  description = "ARN del secreto de PostgreSQL/TimescaleDB (sin revelar el valor)"
  value       = aws_secretsmanager_secret.postgres.arn
}

output "app_git_ref" {
  description = "SHA Git desplegado en la instancia"
  value       = var.app_git_ref
}
