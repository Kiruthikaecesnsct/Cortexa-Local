# Contract: {name}

A shape that an in-progress item defines and other items build against, before the defining code lands. Prevents a client being built against a guessed shape.

Filename: `contracts/{name}.md` (e.g. `contracts/auth-login-response.md`). Dropped when the defining item merges - after that the real code is the source of truth.

- Defined by: {ITEM_ID}
- Consumed by: {ITEM_ID}, {ITEM_ID}
- Status: draft | stable | superseded by {name}

## Shape

```
POST /auth/login
Request:  { "email": string, "password": string }
Response: { "token": string, "expiresIn": number }
Errors:   401 invalid credentials, 429 rate limited
```

## Notes

- Anything a consumer needs that the shape alone does not convey (nullability, units, ordering, pagination).
