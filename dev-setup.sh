#!/bin/bash
# Local Development Setup Script (for dev containers, GitHub Codespaces, etc.)
# This script sets up a complete development environment with .NET 9 and Redis

set -e  # Exit on error

echo "========================================="
echo "RedisKit Dev Container Setup"
echo "========================================="

# Detect OS
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    OS="linux"
    ARCH=$(uname -m)
    if [ "$ARCH" = "aarch64" ]; then
        ARCH="arm64"
    elif [ "$ARCH" = "x86_64" ]; then
        ARCH="x64"
    fi
elif [[ "$OSTYPE" == "darwin"* ]]; then
    OS="osx"
    ARCH=$(uname -m)
    if [ "$ARCH" = "arm64" ]; then
        ARCH="arm64"
    else
        ARCH="x64"
    fi
fi

echo "🔍 Detected OS: $OS-$ARCH"

# Function to install .NET 9
install_dotnet() {
    echo "📦 Installing .NET 9 SDK..."
    
    if command -v dotnet &> /dev/null; then
        CURRENT_VERSION=$(dotnet --version 2>/dev/null || echo "0")
        if [[ $CURRENT_VERSION == 9.* ]]; then
            echo "✅ .NET 9 is already installed: $CURRENT_VERSION"
            return
        fi
    fi
    
    # Download and install .NET 9
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 9.0 --install-dir ~/.dotnet
    
    # Add to PATH
    export DOTNET_ROOT=$HOME/.dotnet
    export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
    
    # Make it permanent
    if [[ "$SHELL" == *"zsh"* ]]; then
        echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.zshrc
        echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.zshrc
    else
        echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
        echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.bashrc
    fi
    
    echo "✅ .NET 9 SDK installed"
}

# Function to install Redis
install_redis() {
    echo "📦 Installing Redis..."
    
    if command -v redis-server &> /dev/null; then
        echo "✅ Redis is already installed: $(redis-server --version | head -n1)"
        return
    fi
    
    if [[ "$OS" == "linux" ]]; then
        # Linux installation
        if command -v apt-get &> /dev/null; then
            sudo apt-get update -qq
            sudo apt-get install -y redis-server redis-tools
        elif command -v yum &> /dev/null; then
            sudo yum install -y redis
        elif command -v dnf &> /dev/null; then
            sudo dnf install -y redis
        else
            # Build from source as fallback
            echo "📦 Building Redis from source..."
            wget http://download.redis.io/redis-stable.tar.gz
            tar xzf redis-stable.tar.gz
            cd redis-stable
            make
            sudo make install
            cd ..
            rm -rf redis-stable redis-stable.tar.gz
        fi
    elif [[ "$OS" == "osx" ]]; then
        # macOS installation
        if command -v brew &> /dev/null; then
            brew install redis
        else
            echo "⚠️  Homebrew not found. Please install Redis manually."
            return 1
        fi
    fi
    
    echo "✅ Redis installed"
}

# Function to start Redis
start_redis() {
    echo "🚀 Starting Redis..."
    
    if [[ "$OS" == "linux" ]]; then
        if command -v systemctl &> /dev/null; then
            sudo systemctl start redis || sudo service redis-server start || redis-server --daemonize yes
        else
            redis-server --daemonize yes
        fi
    elif [[ "$OS" == "osx" ]]; then
        if command -v brew &> /dev/null; then
            brew services start redis || redis-server --daemonize yes
        else
            redis-server --daemonize yes
        fi
    fi
    
    # Wait for Redis to start
    sleep 2
    
    # Verify Redis is running
    if redis-cli ping &> /dev/null; then
        echo "✅ Redis is running"
    else
        echo "⚠️  Redis failed to start"
        return 1
    fi
}

# Main installation flow
install_dotnet
install_redis
start_redis

# Setup the project
echo "📦 Restoring NuGet packages..."
dotnet restore

echo "🔨 Building solution..."
dotnet build --no-restore

echo "🧪 Running unit tests..."
dotnet test --no-build --filter "Category!=Integration" || true

# Create helpful aliases
echo ""
echo "💡 Creating helpful aliases..."
cat > ~/.redis_aliases << 'EOF'
# RedisKit Development Aliases
alias dk-build='dotnet build'
alias dk-test='dotnet test --filter "Category!=Integration"'
alias dk-test-all='dotnet test'
alias dk-run='dotnet run --project RedisKit.Example'
alias dk-watch='dotnet watch --project RedisKit.Example'
alias dk-clean='dotnet clean && rm -rf **/bin **/obj'
alias dk-restore='dotnet restore'
alias dk-pack='dotnet pack -c Release'

# Redis aliases
alias redis-start='redis-server --daemonize yes'
alias redis-stop='redis-cli shutdown'
alias redis-status='redis-cli ping'
alias redis-monitor='redis-cli monitor'
alias redis-flush='redis-cli flushall'
EOF

if [[ "$SHELL" == *"zsh"* ]]; then
    echo 'source ~/.redis_aliases' >> ~/.zshrc
    source ~/.redis_aliases
else
    echo 'source ~/.redis_aliases' >> ~/.bashrc
    source ~/.redis_aliases
fi

# Display summary
echo ""
echo "========================================="
echo "✅ Development Environment Ready!"
echo "========================================="
echo "📍 .NET Version: $(dotnet --version)"
echo "📍 Redis: $(redis-cli ping && echo "Running" || echo "Not running")"
echo ""
echo "🚀 Quick Start Commands:"
echo "  dk-build     - Build the solution"
echo "  dk-test      - Run unit tests"
echo "  dk-test-all  - Run all tests (including integration)"
echo "  dk-run       - Run example project"
echo "  dk-watch     - Run with hot reload"
echo ""
echo "🔧 Redis Commands:"
echo "  redis-cli    - Connect to Redis"
echo "  redis-status - Check Redis status"
echo "  redis-flush  - Clear all data"
echo "========================================="