# Commit Message Guidelines

This project follows the **Conventional Commits** specification to keep a clean, readable Git history.

Format:
```
<type>(optional scope): <short summary>

(optional body)

(optional footer)
```

---

## Types

- **feat**: A new feature
- **fix**: A bug fix
- **docs**: Documentation only changes (e.g., README, XML comments, Swagger)
- **style**: Changes that do not affect meaning of the code (formatting, missing semicolons, etc.)
- **refactor**: Code change that neither fixes a bug nor adds a feature
- **test**: Adding or updating tests
- **chore**: Changes to build process, CI/CD, dependencies, or auxiliary tools

---

## Examples

### Features
```
feat(api): add CORS support for frontend integration
```

### Bug Fixes
```
fix(api): correct null reference in UpdateNoteRequest handler
```

### Documentation
```
docs(api): add XML comments for CRUD endpoints and improve README
```

### Style
```
style: format code with dotnet formatter
```

### Refactor
```
refactor(api): simplify pagination logic in GetAll endpoint
```

### Tests
```
test(api): add integration tests for NotesController
```

### Chore
```
chore(ci): update GitHub Actions workflow to use .NET 8.0.2
```

---

## Tips

- Keep the summary short (max ~72 characters).
- Use **imperative mood** ("add" not "added").
- Use lowercase for type and scope.
- Scope is optional, but recommended (e.g., `api`, `frontend`, `ci`).

---

For more details, see [Conventional Commits](https://www.conventionalcommits.org).
