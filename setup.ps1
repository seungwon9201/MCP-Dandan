# 82ch Unified Setup Script (Windows PowerShell)

Write-Host "========================================"
Write-Host "82ch - MCP Security Framework Setup"
Write-Host "========================================"
Write-Host ""

# Check Docker installation
try {
    $dockerVersion = docker --version
    Write-Host "Docker: $dockerVersion" -ForegroundColor Green
} catch {
    Write-Host "Docker is not installed" -ForegroundColor Red
    Write-Host "Please install Docker Desktop: https://www.docker.com/products/docker-desktop/"
    Write-Host ""
    Write-Host "Or run without Docker:" -ForegroundColor Yellow
    Write-Host "  pip install -r requirements.txt"
    Write-Host "  python server.py"
    exit 1
}

# Check if Docker Desktop is running
try {
    docker ps | Out-Null
} catch {
    Write-Host "Docker Desktop is not running" -ForegroundColor Red
    Write-Host "Please start Docker Desktop and try again."
    Write-Host ""
    Write-Host "Or run without Docker:" -ForegroundColor Yellow
    Write-Host "  pip install -r requirements.txt"
    Write-Host "  python server.py"
    exit 1
}

try {
    $composeVersion = docker-compose --version
    Write-Host "Docker Compose: $composeVersion" -ForegroundColor Green
} catch {
    Write-Host "Docker Compose is not installed" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Stop existing containers
Write-Host "Stopping existing containers..." -ForegroundColor Yellow
docker-compose down 2>$null
Write-Host ""

# Create data directory
Write-Host "Creating data directory..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "data" | Out-Null
Write-Host ""

# Build and run Docker image
Write-Host "Building and running Docker image..." -ForegroundColor Yellow
docker-compose up -d --build
Write-Host ""

# Wait for container to start
Write-Host "Waiting for container to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 5
Write-Host ""

# Check status
Write-Host "Service status:" -ForegroundColor Cyan
docker-compose ps
Write-Host ""

# Display connection info
Write-Host "========================================"
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "========================================"
Write-Host ""
Write-Host "HTTP Server: http://localhost:28173"
Write-Host "Database Path: .\data\mcp_observer.db"
Write-Host ""
Write-Host "Commands:"
Write-Host "  - View logs: docker-compose logs -f"
Write-Host "  - Query DB: python query_db.py"
Write-Host "  - Stop: docker-compose down"
Write-Host ""