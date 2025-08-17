# Security Policy

## Supported Versions

We release patches for security vulnerabilities. Which versions are eligible for receiving such patches depends on the CVSS v3.0 Rating:

| Version | Supported          |
| ------- | ------------------ |
| 1.1.x   | :white_check_mark: |
| 1.0.x   | :x:                |
| < 1.0   | :x:                |

## Reporting a Vulnerability

We take the security of RedisKit seriously. If you believe you have found a security vulnerability, please report it to us as described below.

### Please do NOT:
- Open a public GitHub issue
- Post about it publicly on social media
- Exploit the vulnerability in production environments

### Please DO:
- Email us at: security@rediskit.com (or create a security advisory on GitHub)
- Include the following information:
  - Type of vulnerability (e.g., buffer overflow, SQL injection, cross-site scripting, etc.)
  - Full paths of source file(s) related to the vulnerability
  - The location of the affected source code (tag/branch/commit or direct URL)
  - Any special configuration required to reproduce the issue
  - Step-by-step instructions to reproduce the issue
  - Proof-of-concept or exploit code (if possible)
  - Impact of the issue, including how an attacker might exploit it

### What to expect:
- We will acknowledge your email within 48 hours
- We will confirm the vulnerability and determine its impact within 7 days
- We will release a fix as soon as possible, depending on complexity
- We will credit you in the security advisory (unless you prefer to remain anonymous)

## Security Update Process

When we release a security update:
1. We will create a GitHub Security Advisory
2. We will release a new version with the fix
3. We will update the NuGet package
4. We will notify users through GitHub and our communication channels

## Dependencies

This project uses Dependabot to keep dependencies up to date. Security updates are automatically created as pull requests when vulnerabilities are discovered in our dependencies.

### Key Dependencies:
- **StackExchange.Redis**: Core Redis client library
- **MessagePack**: High-performance serialization
- **System.Text.Json**: JSON serialization
- **Microsoft.Extensions.***: .NET platform extensions

We regularly review and update our dependencies to ensure they are secure and up-to-date.

## Best Practices for Users

When using RedisKit in production:

1. **Always use the latest version**: Security patches are only provided for supported versions
2. **Use secure connections**: Always use SSL/TLS when connecting to Redis in production
3. **Validate input**: Always validate and sanitize user input before storing in Redis
4. **Use authentication**: Always use Redis AUTH in production environments
5. **Limit access**: Use Redis ACLs to limit access to specific commands and keys
6. **Monitor logs**: Regularly monitor Redis and application logs for suspicious activity
7. **Keep Redis updated**: Ensure your Redis server is running a supported, secure version

## Security Features

RedisKit includes several security features:

- **Connection string sanitization**: Passwords are never logged
- **Secure serialization**: Protection against deserialization attacks
- **Input validation**: All inputs are validated before processing
- **Circuit breaker**: Prevents cascade failures and potential DoS
- **Rate limiting support**: Can be configured to prevent abuse
- **Secure defaults**: Secure settings are enabled by default

## Contact

For security concerns, please email: security@rediskit.com

For general questions, please open a GitHub issue.

## Acknowledgments

We would like to thank the following individuals for responsibly disclosing security issues:

<!-- Security researchers will be listed here -->