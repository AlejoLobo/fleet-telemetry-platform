data "aws_iam_policy_document" "ec2_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "instance" {
  name               = "${var.project_name}-dev-ec2-role"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume.json
}

resource "aws_iam_role_policy_attachment" "ssm" {
  role       = aws_iam_role.instance.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

data "aws_iam_policy_document" "read_postgres_secret" {
  statement {
    sid       = "ReadDevPostgresSecret"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.postgres.arn]
  }
}

resource "aws_iam_role_policy" "read_postgres_secret" {
  name   = "${var.project_name}-dev-read-postgres-secret"
  role   = aws_iam_role.instance.id
  policy = data.aws_iam_policy_document.read_postgres_secret.json
}

resource "aws_iam_instance_profile" "instance" {
  name = "${var.project_name}-dev-ec2-profile"
  role = aws_iam_role.instance.name
}

resource "aws_instance" "compose" {
  ami                         = var.ami_id
  instance_type               = var.instance_type
  subnet_id                   = aws_subnet.public[0].id
  vpc_security_group_ids      = [aws_security_group.instance.id]
  iam_instance_profile        = aws_iam_instance_profile.instance.name
  associate_public_ip_address = true

  root_block_device {
    volume_size           = var.root_volume_size_gb
    volume_type           = "gp3"
    encrypted             = true
    delete_on_termination = true
  }

  user_data = templatefile("${path.module}/user-data.sh.tftpl", {
    aws_region             = var.aws_region
    app_git_repository     = var.app_git_repository
    app_git_ref            = var.app_git_ref
    docker_compose_version = var.docker_compose_version
    secret_arn             = aws_secretsmanager_secret.postgres.arn
    alb_dns_name           = aws_lb.dev.dns_name
  })

  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 2
  }

  tags = {
    Name = "${var.project_name}-dev-compose"
  }

  depends_on = [
    aws_internet_gateway.dev,
    aws_secretsmanager_secret_version.postgres,
  ]
}
