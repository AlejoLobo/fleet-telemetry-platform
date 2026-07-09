output "vpc_id" {
  value = aws_vpc.main.id
}

output "rds_endpoint" {
  value = aws_db_instance.fleet.endpoint
}

output "ecs_cluster_name" {
  value = aws_ecs_cluster.fleet.name
}

output "public_subnet_ids" {
  value = aws_subnet.public[*].id
}
