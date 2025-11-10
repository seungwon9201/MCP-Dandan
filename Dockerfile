# 82ch - Unified MCP Security Framework
FROM python:3.11-slim

WORKDIR /app

# Install required packages
RUN apt-get update && apt-get install -y \
    sqlite3 \
    && rm -rf /var/lib/apt/lists/*

# Copy and install Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY . .

# Create data directory
RUN mkdir -p /app/data

# Database volume
VOLUME ["/app/data"]

# Expose HTTP server port
EXPOSE 28173

# Run unified server
CMD ["python", "server.py"]