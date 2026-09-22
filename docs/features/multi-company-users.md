# Multi-Company Users (Bir Kullanıcıyı İki Şirkette Tanımlama)

## Özet
SenseİK'de bir kişi birden fazla şirkette (tenant) login hesabına ve çalışan kaydına sahip olabilir.

## Akış Diyagramları

### 1. Login Flow ✅ (Mevcut: web/src/pages/auth/login.tsx)

```
Kullanıcı Giriş
    ↓
Email + Password
    ↓
API: POST /auth/login
    { email, password, tenantId? }
    ↓
Backend Kontrolü:
    ├─ Email'in bu tenant'ta user'ı var mı?
    ├─ Şifre doğru mu?
    └─ Şirket(ler) sayısı?
    ↓
Eğer 1 Şirket → Direct Login (modal yok)
Eğer 2+ Şirket → TENANT SEÇIM MODAL (mevcut koddaki Modal)
    ↓
Modal: "Aşağıdakilerden birini seçiniz:"
  ☐ ABC Şirketi
  ☐ XYZ Ltd.
  ☐ Yeni Şirket
    ↓
Seçim → handleSelectTenant()
    ↓
Login Complete (Token + TenantId)
```

**Mevcut Kod Lokasyonu**:
- Seçim Modal: `web/src/pages/auth/login.tsx` satır 511-549
- State: `tenantChoices` (satır 162)
- Handler: `handleSelectTenant()` (satır 195-213)

---

### 2. User Create Flow (Yapılacak)

```
Admin: /settings/identity/users/create

Form:
├─ Company Select [▼ Dropdown]  ← Mandatory
├─ Email [john@example.com]
├─ First Name [John]
├─ Last Name [Doe]
└─ Submit Button

On Submit:
    POST /identity/admin/users
    {
      email: "john@example.com",
      firstName: "John",
      lastName: "Doe",
      tenantId: "company-a-uuid"  ← From dropdown
    }
    
Backend:
    Email Uniqueness Check:
    (TenantId, NormalizedEmail) UNIQUE
    
    ✓ john@example.com ABC'de user yok → Create
    ✓ john@example.com XYZ'de user yok → Create
    ✗ john@example.com ABC'de var → Error
```

---

### 3. Multi-Tenant User Scenario

```
Person: john@example.com

Scenario A: Single Company
├─ User (id=u1, tenantId=ABC, email=john@...)
└─ Employee (id=e1, tenantId=ABC, userId=u1)

Scenario B: Multiple Companies
├─ Company ABC:
│  ├─ User (id=u1, tenantId=ABC, email=john@...)
│  └─ Employee (id=e1, tenantId=ABC, userId=u1)
│
├─ Company XYZ:
│  ├─ User (id=u2, tenantId=XYZ, email=john@...)
│  └─ Employee (id=e2, tenantId=XYZ, userId=u2)
│
└─ Different passwords per company:
   ABC: password1
   XYZ: password2
```

---

## Database Changes

### Migration: Email Uniqueness (TenantId Composite)

```sql
-- Remove old single-column unique index
DROP INDEX IF EXISTS IX_User_NormalizedEmail;

-- Add new composite unique index
CREATE UNIQUE INDEX IX_User_TenantId_NormalizedEmail
  ON senseik_identity."User"(tenant_id, normalized_email);
```

### EF Core Configuration

```csharp
// In IdentityDbContext.OnModelCreating()
modelBuilder.Entity<User>()
    .HasIndex(u => new { u.TenantId, u.NormalizedEmail })
    .IsUnique()
    .HasDatabaseName("IX_User_TenantId_NormalizedEmail");

// Remove old index if exists:
// .HasIndex(u => u.NormalizedEmail)
//     .IsUnique()
//     .HasDatabaseName("IX_User_NormalizedEmail")
```

---

## API Endpoints (Backend)

### 1. Login (Existing)
```
POST /auth/login

Request:
{
  "email": "john@example.com",
  "password": "secret123",
  "tenantId": "uuid-of-tenant"  // Optional if only 1 tenant
}

Response (if 2+ tenants):
{
  "status": "select_tenant",
  "tenants": [
    { "tenantId": "abc-uuid", "name": "ABC Corp", "slug": "abc-corp" },
    { "tenantId": "xyz-uuid", "name": "XYZ Ltd", "slug": "xyz-ltd" }
  ]
}

Response (success):
{
  "accessToken": "jwt...",
  "refreshToken": "...",
  "user": { "id": "...", "email": "...", "firstName": "...", "lastName": "..." },
  "tenant": { "id": "...", "name": "...", "slug": "..." }
}
```

### 2. Create User (Admin)
```
POST /identity/admin/users

Request:
{
  "email": "john@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "tenantId": "abc-uuid"
}

Validation:
- Email format valid
- TenantId exists
- (TenantId, NormalizedEmail) not exists
- Caller is admin for this tenant

Response:
{
  "id": "new-user-uuid",
  "email": "john@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "tenantId": "abc-uuid",
  "status": "Invited"
}
```

### 3. List Users (Admin, Tenant-Filtered)
```
GET /identity/admin/users?tenantId={id}

Response:
[
  {
    "id": "u1",
    "email": "john@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "tenantId": "abc-uuid",
    "tenantName": "ABC Corp",
    "status": "Active"
  },
  {
    "id": "u2",
    "email": "john@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "tenantId": "xyz-uuid",
    "tenantName": "XYZ Ltd",
    "status": "Active"
  }
]

Note: Aynı email iki satırda görtülür (farklı tenant'lar için)
```

---

## Frontend Components

### UserCreateForm Pseudo-Code
```typescript
export function UserCreateForm() {
  const form = useForm({
    tenantId: '',   // REQUIRED
    email: '',
    firstName: '',
    lastName: ''
  })
  
  const handleSubmit = async (values) => {
    await apiClient.post('/identity/admin/users', {
      tenantId: values.tenantId,  // TenantId first (most important)
      email: values.email,
      firstName: values.firstName,
      lastName: values.lastName
    })
  }
  
  return (
    <form onSubmit={form.onSubmit(handleSubmit)}>
      <Select
        label="Şirket"
        required
        data={tenants.map(t => ({ value: t.id, label: t.name }))}
        {...form.getInputProps('tenantId')}
      />
      <TextInput label="Email" required {...} />
      <TextInput label="Ad" required {...} />
      <TextInput label="Soyadı" required {...} />
      <Alert>
        ℹ️ Aynı email'i başka şirkette de kullanabilirsiniz
      </Alert>
      <Button type="submit">Oluştur</Button>
    </form>
  )
}
```

### UsersList Pseudo-Code
```typescript
export function UsersList() {
  const [tenantId, setTenantId] = useState('')
  const [users, setUsers] = useState([])
  
  useEffect(() => {
    if (!tenantId) return
    
    apiClient.get(`/identity/admin/users?tenantId=${tenantId}`)
      .then(res => setUsers(res.data))
  }, [tenantId])
  
  // Aynı email bir çok satırda olabilir:
  // john@example.com → ABC Corp
  // john@example.com → XYZ Ltd
  
  return (
    <>
      <Select
        label="Şirketi Filtrele"
        data={tenants.map(t => ({ value: t.id, label: t.name }))}
        value={tenantId}
        onChange={setTenantId}
      />
      <Table>
        {users.map(u => (
          <tr key={u.id}>
            <td>{u.email}</td>
            <td>{u.firstName} {u.lastName}</td>
            <td>{u.tenantName}</td>
            <td>{u.status}</td>
          </tr>
        ))}
      </Table>
    </>
  )
}
```

---

## Translation Keys (i18n)

```json
{
  "settings": {
    "createUser": "Kullanıcı Oluştur",
    "company": "Şirket",
    "selectCompany": "Şirket seçiniz",
    "multiCompanyNote": "Çok Şirkette Kullanıcı",
    "multiCompanyNoteHint": "Aynı email adresiyle başka bir şirkette de kullanıcı oluşturabilirsiniz",
    "userCreatedSuccessfully": "Kullanıcı başarıyla oluşturuldu",
    "redirectingToUsers": "Kullanıcılar sayfasına yönlendiriliyorsunuz..."
  },
  "auth": {
    "selectCompany": {
      "title": "Şirketinizi Seçin",
      "subtitle": "Hangi şirkete giriş yapmak istiyorsunuz?"
    }
  }
}
```

---

## Security Considerations

1. **TenantId Validation**: Her API call'ında tenant authorization check
2. **Email Ownership**: User creation'da email ownership verification (mevcut akış)
3. **Password Reset**: Her tenant'da ayrı password reset token flow
4. **Session**: JWT token'da TenantId embedded, tenant switch'de new token
5. **Audit Logging**: User creation/login timestamp + TenantId

---

## Implementation Order

1. **Database**: Migration (TenantId composite unique) ✓ Planned
2. **Backend API**: Endpoints (login, create user, list users) ✓ Planned
3. **Login UI**: Modal (✅ Already done in web/src/pages/auth/login.tsx)
4. **Admin UI**: UserCreateForm component
5. **Admin UI**: UsersList component with tenant filter
6. **Tests**: Multi-tenant user scenarios
7. **Translations**: i18n keys

---

## Testing Scenarios

| Scenario | Expected |
|----------|----------|
| Login with 1 company | No modal, direct login |
| Login with 2+ companies | Show modal, user selects |
| Create user in ABC | Only ABC's employees visible |
| Create user ABC + XYZ with same email | Both exist, different User.Id |
| Login attempt wrong password ABC | Fail |
| Login john ABC, then switch to XYZ | Logout ABC, login XYZ |
| Password reset in ABC | Only ABC's password resets |

---

## Current Status

- ✅ **Login Modal**: Already implemented in `web/src/pages/auth/login.tsx`
- ⏳ **Email Uniqueness Migration**: Needs database migration
- ⏳ **Backend Endpoints**: Needs auth service updates
- ⏳ **User Management UI**: Needs UserCreateForm + UsersList
- ⏳ **Tests**: Needs multi-tenant scenarios
- ⏳ **Translations**: Needs i18n keys
