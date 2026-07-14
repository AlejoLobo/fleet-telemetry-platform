output "vpc_id" {
  description = "ID de la VPC del blueprint conceptual"
  value       = aws_vpc.main.id
}

output "public_subnet_ids" {
  description = "Subnets públicas del blueprint"
  value       = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  description = "Subnets privadas del blueprint (RDS / cómputo futuro)"
  value       = aws_subnet.private[*].id
}

output "ecs_cluster_name" {
  description = "Nombre del cluster ECS del blueprint (sin services)"
  value       = aws_ecs_cluster.fleet.name
}

output "db_endpoint" {
  description = "Endpoint RDS PostgreSQL 16 (host:port) — NO incluye TimescaleDB; ver infra/README.md"
  value       = aws_db_instance.fleet.endpoint
}

output "ecr_api_repository_url" {
  description = "URI del repositorio ECR de la API"
  value       = aws_ecr_repository.api.repository_url
}

output "ecr_worker_repository_url" {
  description = "URI del repositorio ECR del Worker"
  value       = aws_ecr_repository.worker.repository_url
}
