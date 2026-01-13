# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial release of Distributed Rate Limiter
- Global rate limiting across distributed service instances
- Local permit caching for high-performance fast-path decisions
- Bounded queue with backpressure support
- Cancellation token support for queued requests
- Idempotent operations with sequence number tracking
- Automatic idle client purging and permit reclamation
- Comprehensive XML documentation
- Unit tests with xUnit and FluentAssertions
- Integration tests with Orleans TestingHost
- GitHub Actions CI/CD pipeline
- NuGet package configuration

### Technical Details
- Built on .NET 8.0
- Uses Microsoft Orleans 8.2.0
- Implements `System.Threading.RateLimiting.RateLimiter` abstraction
- Supports dependency injection via `Microsoft.Extensions.DependencyInjection`

## [1.0.0] - TBD

### Added
- First stable release

---

## Version History

### Versioning Strategy

This project uses [Semantic Versioning](https://semver.org/):

- **MAJOR** version for incompatible API changes
- **MINOR** version for backwards-compatible functionality additions
- **PATCH** version for backwards-compatible bug fixes

### Pre-release Versions

- `alpha` - Early development, APIs may change significantly
- `beta` - Feature complete, APIs stabilizing
- `rc` - Release candidate, final testing phase
