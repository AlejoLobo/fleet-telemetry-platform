output "vpc_id" {
  value = aws_vpc.main.id
}

output "rds_endpoint" {
  value = aws_db_instance.fleet.endpoint
}

output "ecs_cluster_name" {
  value = aws_ecs_cluster.fleet.name
}

output "ecr_api_repository_url" {
  value = aws_ecr_repository.api.repository_url
}

output "ecr_worker_repository_url" {
  value = aws_ecr_repository.worker.repository_url
}

output "ecs_task_definition_api_arn" {
  value = aws_ecs_task_definition.api.arn
}

output "ecs_task_definition_worker_arn" {
  value = aws_ecs_task_definition.worker.arn
}

output "public_subnet_ids" {
  value = aws_subnet.public[*].id
}
