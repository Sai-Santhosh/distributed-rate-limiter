# Contributing to Distributed Rate Limiter

First off, thank you for considering contributing to Distributed Rate Limiter! It's people like you that make this project such a great tool.

## Code of Conduct

This project and everyone participating in it is governed by our commitment to providing a welcoming and inclusive environment. By participating, you are expected to uphold this commitment.

## How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check the existing issues as you might find out that you don't need to create one. When you are creating a bug report, please include as many details as possible:

- **Use a clear and descriptive title** for the issue to identify the problem.
- **Describe the exact steps which reproduce the problem** in as many details as possible.
- **Provide specific examples to demonstrate the steps**.
- **Describe the behavior you observed after following the steps** and point out what exactly is the problem with that behavior.
- **Explain which behavior you expected to see instead and why.**
- **Include your environment details** (.NET version, Orleans version, OS, etc.).

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion, please include:

- **Use a clear and descriptive title** for the issue to identify the suggestion.
- **Provide a step-by-step description of the suggested enhancement** in as many details as possible.
- **Provide specific examples to demonstrate the steps** or provide mock-ups if applicable.
- **Describe the current behavior** and **explain which behavior you expected to see instead** and why.
- **Explain why this enhancement would be useful** to most users.

### Pull Requests

Please follow these steps to have your contribution considered:

1. **Fork the repository** and create your branch from `main`.
2. **Write clear, concise commit messages.**
3. **Include appropriate tests** for your changes.
4. **Ensure the test suite passes** by running `dotnet test`.
5. **Make sure your code follows the existing style** (we use EditorConfig).
6. **Update documentation** if you're changing behavior.
7. **Fill in the pull request template** when submitting.

## Development Setup

### Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Visual Studio 2022, JetBrains Rider, or VS Code with C# extension

### Getting Started

```bash
# Clone your fork
git clone https://github.com/YOUR_USERNAME/distributed-rate-limiter.git
cd distributed-rate-limiter

# Add the upstream remote
git remote add upstream https://github.com/ORIGINAL_OWNER/distributed-rate-limiter.git

# Install dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Project Structure

```
distributed-rate-limiter/
├── DistributedRateLimiting.Orleans/    # Core library
├── TestApp/                            # Demo application
└── tests/
    ├── DistributedRateLimiting.Orleans.Tests/           # Unit tests
    └── DistributedRateLimiting.Orleans.IntegrationTests/ # Integration tests
```

## Style Guidelines

### C# Code Style

- Follow the conventions defined in `.editorconfig`
- Use file-scoped namespaces
- Use explicit types for complex expressions, `var` for obvious ones
- Add XML documentation to all public APIs
- Use nullable reference types
- Prefer async/await over direct Task manipulation

### Commit Messages

- Use the present tense ("Add feature" not "Added feature")
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit the first line to 72 characters or less
- Reference issues and pull requests liberally after the first line

### Testing

- Write unit tests for all new functionality
- Use descriptive test method names following the pattern: `MethodName_Scenario_ExpectedBehavior`
- Use FluentAssertions for readable assertions
- Aim for high code coverage but prioritize meaningful tests

## Questions?

Feel free to open an issue with your question or reach out to the maintainers.

Thank you for contributing!
