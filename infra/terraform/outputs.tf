output "vpc_id" {
  description = "ID de la VPC del blueprint"
  value       = aws_vpc.main.id
}

output "public_subnet_ids" {
  description = "Subnets públicas (ALB / ingress futuro)"
  value       = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  description = "Subnets privadas (RDS / tasks Fargate)"
  value       = aws_subnet.private[*].id
}

output "ecs_cluster_name" {
  description = "Nombre del cluster ECS"
  value       = aws_ecs_cluster.fleet.name
}

output "db_endpoint" {
  description = "Endpoint RDS PostgreSQL (host:port)"
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

output "ecs_task_definition_api_arn" {
  description = "ARN de la task definition de ejemplo (API) — no es service productivo"
  value       = aws_ecs_task_definition.api.arn
}

output "ecs_task_definition_worker_arn" {
  description = "ARN de la task definition de ejemplo (Worker) — no es service productivo"
  value       = aws_ecs_task_definition.worker.arn
}
