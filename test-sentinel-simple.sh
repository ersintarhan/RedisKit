#!/bin/bash
echo "Testing Sentinel connectivity..."

# Test each Sentinel
for port in 26379 26380 26381; do
    echo "Testing Sentinel on port $port..."
    timeout 2 bash -c "echo 'PING' | docker exec -i sentinel-$(($port - 26378)) redis-cli -p 26379" || echo "Failed on port $port"
done

# Get master from first Sentinel
echo ""
echo "Getting master info from Sentinel..."
docker exec sentinel-1 redis-cli -p 26379 SENTINEL get-master-addr-by-name mymaster

# Test master directly
echo ""
echo "Testing master directly..."
MASTER_IP=$(docker exec sentinel-1 redis-cli -p 26379 SENTINEL get-master-addr-by-name mymaster | head -1)
echo "Master IP: $MASTER_IP"
docker exec redis-master redis-cli -a redis_password INFO server | grep redis_version
