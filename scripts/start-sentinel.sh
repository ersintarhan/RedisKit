#!/bin/bash

# Clean up any existing containers
docker stop redis-master redis-replica-1 redis-replica-2 sentinel-1 sentinel-2 sentinel-3 2>/dev/null
docker rm redis-master redis-replica-1 redis-replica-2 sentinel-1 sentinel-2 sentinel-3 2>/dev/null

# Start Redis Master
docker run -d --name redis-master \
  -p 7379:6379 \
  redis:7-alpine redis-server --requirepass redis_password

# Wait for master to be ready
sleep 2

# Get master IP
MASTER_IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' redis-master)
echo "Master IP: $MASTER_IP"

# Start Redis Replicas
docker run -d --name redis-replica-1 \
  -p 7380:6379 \
  redis:7-alpine redis-server \
  --replicaof $MASTER_IP 6379 \
  --masterauth redis_password \
  --requirepass redis_password

docker run -d --name redis-replica-2 \
  -p 7381:6379 \
  redis:7-alpine redis-server \
  --replicaof $MASTER_IP 6379 \
  --masterauth redis_password \
  --requirepass redis_password

# Wait for replicas
sleep 2

# Start Sentinels with master IP
docker run -d --name sentinel-1 \
  -p 26379:26379 \
  redis:7-alpine sh -c "echo 'port 26379' > /etc/redis-sentinel.conf && \
  echo 'sentinel monitor mymaster $MASTER_IP 6379 2' >> /etc/redis-sentinel.conf && \
  echo 'sentinel auth-pass mymaster redis_password' >> /etc/redis-sentinel.conf && \
  echo 'sentinel down-after-milliseconds mymaster 5000' >> /etc/redis-sentinel.conf && \
  echo 'sentinel parallel-syncs mymaster 1' >> /etc/redis-sentinel.conf && \
  echo 'sentinel failover-timeout mymaster 10000' >> /etc/redis-sentinel.conf && \
  redis-sentinel /etc/redis-sentinel.conf"

docker run -d --name sentinel-2 \
  -p 26380:26379 \
  redis:7-alpine sh -c "echo 'port 26379' > /etc/redis-sentinel.conf && \
  echo 'sentinel monitor mymaster $MASTER_IP 6379 2' >> /etc/redis-sentinel.conf && \
  echo 'sentinel auth-pass mymaster redis_password' >> /etc/redis-sentinel.conf && \
  echo 'sentinel down-after-milliseconds mymaster 5000' >> /etc/redis-sentinel.conf && \
  echo 'sentinel parallel-syncs mymaster 1' >> /etc/redis-sentinel.conf && \
  echo 'sentinel failover-timeout mymaster 10000' >> /etc/redis-sentinel.conf && \
  redis-sentinel /etc/redis-sentinel.conf"

docker run -d --name sentinel-3 \
  -p 26381:26379 \
  redis:7-alpine sh -c "echo 'port 26379' > /etc/redis-sentinel.conf && \
  echo 'sentinel monitor mymaster $MASTER_IP 6379 2' >> /etc/redis-sentinel.conf && \
  echo 'sentinel auth-pass mymaster redis_password' >> /etc/redis-sentinel.conf && \
  echo 'sentinel down-after-milliseconds mymaster 5000' >> /etc/redis-sentinel.conf && \
  echo 'sentinel parallel-syncs mymaster 1' >> /etc/redis-sentinel.conf && \
  echo 'sentinel failover-timeout mymaster 10000' >> /etc/redis-sentinel.conf && \
  redis-sentinel /etc/redis-sentinel.conf"

# Wait for sentinels to be ready
sleep 5

# Verify setup
echo "Checking Redis Master..."
docker exec redis-master redis-cli -a redis_password ping

echo "Checking Sentinels..."
docker exec sentinel-1 redis-cli -p 26379 SENTINEL masters | head -10

echo "Sentinel setup complete!"
echo "Sentinel endpoints: localhost:26379, localhost:26380, localhost:26381"
echo "Service name: mymaster"
echo "Password: redis_password"