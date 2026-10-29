# FunQuery

FunQuery is a small, extensible query language for expressing data queries as functional expression
chains. The same query can run in memory or against a registered provider; PostgreSQL is the first
implemented SQL provider.

## Start here

- [Language overview](DataTypes.md)
- [Operators](Operators.md)
- [Functions](Functions.md)
- [Variables](Variables.md)
- [HTTP API](HttpApi.md)
- [PostgreSQL provider](PostgreSql.md)

## Quick example

```text
$source([
    {id: 1, name: 'hello'},
    {id: 2, name: 'world'}
]).$filter(id eq 2)
```

Result:

```json
[
  { "id": 2, "name": "world" }
]
```

## HTTP

```bash
curl "http://localhost:5000/query?q=%24source(%5B1%2C2%2C3%5D).%24count()"
```

See [HTTP API](HttpApi.md) for GET/POST, variables, errors, limits, and security.

## Developer tools

- [OpenAPI and Scalar docs UI](OpenApi.md)
- [Local PostgreSQL development](Development.md)
- [Self-hosting these docs](SelfHosting.md)
