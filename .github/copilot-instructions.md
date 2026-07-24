# GitHub Copilot Instructions

## Technology Stack

Use only the following technologies unless explicitly instructed otherwise:

- C#
- .NET
- jQuery
- AJAX
- PostgreSQL
- Dapper ORM
- Bootstrap

Do NOT use:

- Vanilla JavaScript
- Blazor
- Entity Framework
- Any frontend framework or library not listed above

---

## UI Components

Always reuse existing UI components whenever possible.

- Reuse the existing Bootstrap modal.
- Reuse the existing confirmation modal.
- Reuse the existing toast notification component.
- Do not create duplicate modal or toast implementations.

---

## Code Quality

Generated code must be:

- Clean
- Simple
- Easy to understand
- Easy to maintain
- Reusable
- Well organized
- Follow the project's existing coding style
- Avoid unnecessary abstraction
- Avoid duplicated code (DRY)
- Keep methods small and focused
- Use meaningful variable and method names

---

## Backend

- Use Dapper ORM for all database access.
- Use parameterized SQL queries.
- Target PostgreSQL compatibility.
- Handle exceptions appropriately.
- Dispose database connections properly.
- Return meaningful error messages.

---

## Frontend

- Use jQuery for DOM manipulation and events.
- Use AJAX for server communication.
- Use Bootstrap components.
- Keep HTML clean and readable.
- Separate HTML, CSS, and jQuery where appropriate.
- Do not use vanilla JavaScript.

---

## Before Completing Any Task

Before considering a task complete, verify that:

- The generated code is free from obvious syntax errors.
- Console errors are resolved.
- Browser console contains no JavaScript errors.
- AJAX requests complete successfully.
- No broken references exist.
- Existing functionality is not broken.

If there are potential issues, fix them before finishing.

---

## General Rules

- Prefer modifying existing code over creating new implementations.
- Reuse existing helper methods.
- Follow existing project architecture.
- Keep changes minimal.
- Do not introduce unnecessary dependencies.
- Generate production-ready code instead of examples.

## Build and Verification Rules

- Do NOT run `dotnet build`.
- Do NOT run `dotnet test`.
- Do NOT execute any build, restore, or publish commands.
- Do NOT assume the project builds successfully.
- Verify code by reviewing it for syntax and logical consistency only.
- If build verification is required, ask me to run it locally instead of attempting it.
