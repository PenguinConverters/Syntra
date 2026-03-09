# Syntra - API Reference

## Overview

The Syntra REST API dynamically exposes database objects (views, stored procedures) as OData-compatible endpoints. All definitions, schemas, and OpenAPI specifications are generated at runtime from the database without compilation.

## Authentication

### Windows Negotiate (On-Premises)
The API uses Windows Integrated Authentication. The authenticated user's identity is passed through to SQL Server for per-record authorization via database functions and views.

### JWT Bearer with OBO Flow (Cloud)
For Azure-hosted deployments:
1. Client acquires token for Syntra API from Azure AD
2. API exchanges token for SQL access token via On-Behalf-Of flow
3. SQL queries execute under the user's identity

Requires `user_impersonation` scope or `Syntra.AccessAsApp` app role.

## Endpoints

### Data Operations

#### GET /api/{entity}
Query entity data with OData parameters.

**Parameters:**
| Name | Type | Description |
|------|------|-------------|
| entity | path | Entity name (maps to `SP_S1FE_{entity}_READ`) |
| $filter | query | OData filter expression |
| $orderby | query | Sorting (e.g., `displayName asc`) |
| $top | query | Max results (default: 100, max: 1500) |
| $skip | query | Offset for pagination |
| $skiptoken | query | Base64-encoded pagination state |
| $select | query | Column projection |
| $apply | query | Aggregation (e.g., `distinct`) |
| @syntra.metadata | query | Metadata level (see below) |

**Metadata Levels:**
| Value | Description |
|-------|-------------|
| (omitted or 0) | Return data rows |
| minimal (1) | Basic metadata only |
| properties (2) | Entity property definitions |
| count (3) | Total record count |
| parameters (4) | Stored procedure parameter definitions |
| 8 | Full field definitions (DataType, DisplayName, RegularExpression, FilterBy) |

**Response:**
```json
{
  "@odata.context": "api/$metadata#ADUser",
  "@odata.count": 1234,
  "value": [
    {
      "objectGUID": "abc-123",
      "displayName": "John Doe",
      "mail": "john@example.com"
    }
  ],
  "@odata.nextLink": "api/ADUser?$skiptoken=eyJ..."
}
```

#### POST /api/{entity}
Create a new record. Maps to `SP_S1FE_{entity}_CREATE`.

**Body:** JSON object with field values.

#### PATCH /api/{entity}
Update an existing record. Maps to `SP_S1FE_{entity}_UPDATE`.

**Body:** JSON object with fields to update.

#### DELETE /api/{entity}
Delete a record. Maps to `SP_S1FE_{entity}_DELETE`.

**Query Parameters:** Primary key values.

### Schema Endpoints

#### GET /api/$metadata
Returns OData CSDL/EDM XML schema.

Dynamically built from `INFORMATION_SCHEMA.COLUMNS` and `INFORMATION_SCHEMA.TABLES`.

**Response:** `application/xml`
```xml
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="PenguinConverters.Syntra">
      <EntityType Name="ADUser">
        <Key>
          <PropertyRef Name="ADUserId"/>
        </Key>
        <Property Name="ADUserId" Type="Edm.Guid" Nullable="false"/>
        <Property Name="displayName" Type="Edm.String" MaxLength="256"/>
        ...
      </EntityType>
      <EntityContainer Name="SyntraContainer">
        <EntitySet Name="ADUser" EntityType="PenguinConverters.Syntra.ADUser"/>
      </EntityContainer>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
```

#### GET /api/openapi.json
Returns dynamically generated OpenAPI 3.0.3 specification.

Discovers available entities from `SP_S1FE_*` stored procedures. Generates:
- Paths with GET/POST/PATCH/DELETE operations
- Component schemas from column metadata
- OData query parameters
- Security scheme definitions

**Response:** `application/json` (OpenAPI 3.0.3 document)

## OData Filter Syntax

### Comparison Operators
```
$filter=displayName eq 'John Doe'
$filter=age gt 25
$filter=salary ge 50000
$filter=status ne 'Inactive'
```

### Logical Operators
```
$filter=department eq 'Engineering' and status eq 'Active'
$filter=department eq 'HR' or department eq 'Finance'
$filter=not (status eq 'Deleted')
```

### String Functions
```
$filter=contains(displayName, 'John')
$filter=startswith(mail, 'admin')
$filter=endswith(mail, '@example.com')
```

### Null Checks
```
$filter=manager eq null
$filter=deletedDate ne null
```

## SQL Type to EDM Type Mapping

| SQL Type | EDM Type |
|----------|----------|
| uniqueidentifier | Edm.Guid |
| varchar, nvarchar, char, nchar, text, ntext | Edm.String |
| int | Edm.Int32 |
| bigint | Edm.Int64 |
| smallint | Edm.Int16 |
| tinyint | Edm.Byte |
| bit | Edm.Boolean |
| decimal, numeric | Edm.Decimal |
| float | Edm.Double |
| real | Edm.Single |
| datetime, datetime2, smalldatetime | Edm.DateTimeOffset |
| date | Edm.Date |
| time | Edm.TimeOfDay |
| varbinary, binary, image | Edm.Binary |
| rowversion, timestamp | Edm.Binary |

## Error Responses

```json
{
  "error": {
    "code": "InvalidFilter",
    "message": "The filter expression contains an unsupported operator."
  }
}
```

| HTTP Status | Description |
|-------------|-------------|
| 200 | Success |
| 400 | Invalid request or filter syntax |
| 401 | Authentication required |
| 403 | Insufficient permissions |
| 404 | Entity not found |
| 500 | Server error |
