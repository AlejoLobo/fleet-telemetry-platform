resource "random_password" "postgres" {
  # Solo alfanumérico para connection strings Compose sin escape.
  length  = 32
  special = false
}

resource "aws_secretsmanager_secret" "postgres" {
  name                    = "${var.project_name}/dev/postgres"
  description             = "Credenciales PostgreSQL/TimescaleDB del entorno Compose de desarrollo"
  recovery_window_in_days = 0

  tags = {
    Name = "${var.project_name}-dev-postgres-secret"
  }
}

resource "aws_secretsmanager_secret_version" "postgres" {
  secret_id = aws_secretsmanager_secret.postgres.id
  secret_string = jsonencode({
    POSTGRES_USER     = "fleet"
    POSTGRES_PASSWORD = random_password.postgres.result
    POSTGRES_DB       = "fleet"
  })
}
