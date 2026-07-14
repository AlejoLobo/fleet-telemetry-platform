# Security groups sin referencias circulares (reglas en recursos separados).

resource "aws_security_group" "alb" {
  name        = "${var.project_name}-dev-alb-sg"
  description = "ALB HTTP publico hacia la instancia Compose"
  vpc_id      = aws_vpc.dev.id

  tags = {
    Name = "${var.project_name}-dev-alb-sg"
  }
}

resource "aws_security_group" "instance" {
  name        = "${var.project_name}-dev-ec2-sg"
  description = "EC2 Docker Compose: solo ALB a Web/API; sin SSH/DB/Kafka publicos"
  vpc_id      = aws_vpc.dev.id

  tags = {
    Name = "${var.project_name}-dev-ec2-sg"
  }
}

resource "aws_vpc_security_group_ingress_rule" "alb_http" {
  for_each = toset(var.allowed_cidr_blocks)

  security_group_id = aws_security_group.alb.id
  description       = "HTTP desde CIDRs permitidos"
  from_port         = 80
  to_port           = 80
  ip_protocol       = "tcp"
  cidr_ipv4         = each.value
}

resource "aws_vpc_security_group_egress_rule" "alb_to_web" {
  security_group_id            = aws_security_group.alb.id
  description                  = "Hacia instancia Web (3000)"
  from_port                    = 3000
  to_port                      = 3000
  ip_protocol                  = "tcp"
  referenced_security_group_id = aws_security_group.instance.id
}

resource "aws_vpc_security_group_egress_rule" "alb_to_api" {
  security_group_id            = aws_security_group.alb.id
  description                  = "Hacia instancia API (5000)"
  from_port                    = 5000
  to_port                      = 5000
  ip_protocol                  = "tcp"
  referenced_security_group_id = aws_security_group.instance.id
}

resource "aws_vpc_security_group_ingress_rule" "instance_web_from_alb" {
  security_group_id            = aws_security_group.instance.id
  description                  = "Web desde ALB"
  from_port                    = 3000
  to_port                      = 3000
  ip_protocol                  = "tcp"
  referenced_security_group_id = aws_security_group.alb.id
}

resource "aws_vpc_security_group_ingress_rule" "instance_api_from_alb" {
  security_group_id            = aws_security_group.instance.id
  description                  = "API desde ALB"
  from_port                    = 5000
  to_port                      = 5000
  ip_protocol                  = "tcp"
  referenced_security_group_id = aws_security_group.alb.id
}

resource "aws_vpc_security_group_egress_rule" "instance_all" {
  security_group_id = aws_security_group.instance.id
  description       = "Salida a Internet (imagenes, git, paquetes)"
  ip_protocol       = "-1"
  cidr_ipv4         = "0.0.0.0/0"
}
