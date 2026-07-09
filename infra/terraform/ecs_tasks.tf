variable "container_image_api" {
  description = "Imagen Docker de la API (ECR URI:tag)"
  type        = string
  default     = ""
}

variable "container_image_worker" {
  description = "Imagen Docker del Worker (ECR URI:tag)"
  type        = string
  default     = ""
}

resource "aws_iam_role" "ecs_task_execution" {
  name = "${var.project_name}-${var.environment}-ecs-exec"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action    = "sts:AssumeRole"
      Effect    = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
    }]
  })
}

resource "aws_iam_role_policy_attachment" "ecs_task_execution" {
  role       = aws_iam_role.ecs_task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_ecs_task_definition" "api" {
  family                   = "${var.project_name}-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_task_execution.arn

  container_definitions = jsonencode([{
    name      = "api"
    image     = var.container_image_api != "" ? var.container_image_api : "${aws_ecr_repository.api.repository_url}:latest"
    essential = true
    portMappings = [{ containerPort = 5000, protocol = "tcp" }]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.api.name
        "awslogs-region"        = var.aws_region
        "awslogs-stream-prefix" = "api"
      }
    }
    environment = [
      { name = "ASPNETCORE_ENVIRONMENT", value = var.environment },
      { name = "Kafka__BootstrapServers", value = "REPLACE_WITH_MSK_OR_KAFKA_ENDPOINT" },
      { name = "TimescaleDb__ConnectionString", value = "REPLACE_WITH_RDS_CONNECTION_STRING" }
    ]
  }])
}

resource "aws_ecs_task_definition" "worker" {
  family                   = "${var.project_name}-worker"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_task_execution.arn

  container_definitions = jsonencode([{
    name      = "worker"
    image     = var.container_image_worker != "" ? var.container_image_worker : "${aws_ecr_repository.worker.repository_url}:latest"
    essential = true
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.api.name
        "awslogs-region"        = var.aws_region
        "awslogs-stream-prefix" = "worker"
      }
    }
    environment = [
      { name = "DOTNET_ENVIRONMENT", value = var.environment },
      { name = "Kafka__BootstrapServers", value = "REPLACE_WITH_MSK_OR_KAFKA_ENDPOINT" },
      { name = "TimescaleDb__ConnectionString", value = "REPLACE_WITH_RDS_CONNECTION_STRING" }
    ]
  }])
}
