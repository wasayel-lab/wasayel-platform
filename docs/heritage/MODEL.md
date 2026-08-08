# The Operation-Accounting Model (OAM)

## Origin

The model began as an observation about financial operations: a sale is not
one action but a tree of double-entry journal entries — cash ↔ customer,
customer ↔ revenue, revenue ↔ inventory, plus tax, discount, commission
entries. Each entry has a debit party and a credit party. The books balance
or they don't, and when they don't you know something is wrong.

The second observation: in e-commerce the compound transaction decomposes
across time and actors — cart, payment, preparation, pickup, delivery — each
step producing its own entry and each entry linked to the previous by
accrual/fulfillment relations. The vendor's acceptance is a fulfillment of
the customer's payment accrual. The driver's delivery confirmation is a
fulfillment of the vendor's dispatch accrual.

The third observation: accounting reports (trial balance, income statement,
balance sheet) and accounts (customer account, cash account) are themselves
half-entries — they are one side of many entries, aggregated or constrained.
A customer account is the sum of all parties where the customer appears. A
revenue report is the sum of credit-side parties tagged as revenue in a date
range.

The fourth observation: this structure is not limited to money. A chat
message is an entry: sender transfers one message to recipient. Delivery
receipts, read receipts, listen confirmations — each is an
accrual/fulfillment pair. Notifications, authentication challenges, file
uploads — all transfer a value from one party to another under constraints.

The fifth observation: since the value is no longer numeric, the constraint
functions must generalize beyond arithmetic comparison. They become arbitrary
predicates (analyzers) and cross-cutting rules (interceptors). The
aggregation functions become arbitrary reducers over party streams.

This gives us a computational model where the **atomic unit is the party**
(identity + value + tags), the **molecular unit is the entry** (a pair of
parties under constraints), and **everything else — accounts, reports,
workflows, policies — is derived** from entries through aggregation,
filtering, and composition.

---

## Core concepts

### Party (الطرف)

The atom. An identity, a value, and a set of tags.

```
Party = (Identity: string, Value: decimal, Tags: {key: value})
```

- `Identity`: conventional format `Kind:id` (e.g. `User:abc`, `Vendor:xy`)
- `Value`: whatever makes sense — money, count, weight, 1-for-boolean
- `Tags`: flat key-value pairs — `role`, `direction`, `currency`, etc.

A party alone is a half-entry. An account is a collection of parties with
the same identity across many entries.

### Entry / Operation (القيد)

The molecule. A pair of parties (debit and credit) with tags, constraints,
an execute body, and optional sub-entries.

```
Entry = {
    Type: string,
    Parties: [Party],           -- at least one From (debit) and one To (credit)
    Tags: {key: value},         -- metadata visible to interceptors and consumers
    Analyzers: [Analyzer],      -- local constraints (run before/after execute)
    Execute: Context → Task,    -- the actual state change
    SubEntries: [Entry],        -- composition (tree structure)
    Relations: [Relation],      -- links to other entries (accrual/fulfillment/reversal)
}
```

The `AccountingBuilder` (`Entry.Create(type)`) enforces:
- At least one `.From()` (debit party) and one `.To()` (credit party)
- Automatic `BalanceAnalyzer` (sum of debit values = sum of credit values)
- Tag `pattern: accounting` on every entry

### Analyzer (المحلل)

A local constraint bound to a single entry via `.Analyze()` or
`.PostAnalyze()`. Runs inside the engine's lifecycle. Returns Pass or Fail.

```
Analyzer = {
    Name: string,
    WatchedTagKeys: [string],   -- empty = always runs
    AnalyzeAsync: Context → AnalyzerResult
}
```

**When to use**: the constraint belongs to THIS entry specifically.
Examples: required field, max length, range check, balance check.

Built-in analyzers: `RequiredFieldAnalyzer`, `RangeAnalyzer`,
`MaxLengthAnalyzer`, `ConditionAnalyzer`, `PredicateAnalyzer`,
`BalanceAnalyzer`, `FulfillmentAnalyzer`.

### Interceptor (المعترض)

A global constraint registered in DI, applied to any matching entry at
runtime. Same interface as an analyzer but bound by predicate, not by
builder call.

```
Interceptor = {
    Name: string,
    Phase: Pre | Post | Both,
    AppliesTo: Entry → bool,
    InterceptAsync: Context → AnalyzerResult
}
```

**When to use**: the constraint is cross-cutting — shared across many entry
types. Examples: quota enforcement, permission check, audit logging, rate
limiting, translation.

**Blocking injection**: `entry.Sealed()` blocks all interceptors.
`entry.ExcludeInterceptor("name")` blocks one by name.

### ProviderContract (عقد المزود)

A mandatory external dependency that an entry's execute body needs. Defined
by the library, implemented by the consuming application.

Unlike an interceptor (optional, transparent, global), a provider contract
is:
- **Mandatory** — the entry cannot execute without it
- **Explicit** — the entry declares its need via `.Requires<T>()`
- **Typed** — the contract has a specific interface with specific methods

Examples: `IMessageStore` (persistence), `IDeliveryTransport` (delivery),
`IPaymentGateway` (charging), `ISmsSender` (OTP delivery).

In the current implementation, provider contracts are resolved from DI
inside the execute body via `ctx.Services.GetRequiredService<T>()`. The
conceptual distinction matters for library design even though the
resolution mechanism is standard DI.

### Relation (العلاقة)

A link between two entries expressing temporal or logical dependency:

- **Fulfillment**: entry B completes entry A (delivery fulfills payment)
- **PartialFulfillment**: entry B partially completes entry A
- **Reversal**: entry B undoes entry A (refund reverses charge)
- **Amendment**: entry B modifies entry A

Relations enable the accrual/fulfillment pattern: entry A creates an
obligation, entry B fulfills it. A `FulfillmentAnalyzer` can verify that
the original entry exists and is in the correct state.

### Account (الحساب)

A derived concept — not stored, computed. An account is the collection of
all parties with a given identity across all entries. A customer account is
every party where `Identity = "User:abc"`. The balance is the sum of credit
values minus debit values (or vice versa depending on account type).

Accounts are currently implicit (no dedicated store). Making them explicit
(a queryable Party store) is a future enhancement that enables reporting
without scanning all entries.

### OperationEnvelope (المغلف)

The wire format. Every HTTP response wraps data in:

```
OperationEnvelope<T> = {
    Data: T,
    Operation: OperationDescriptor,  -- type, status, parties, tags, analyzers
    Error: OperationError?,          -- code, message, hint, details
    Meta: object?
}
```

This keeps backend and frontend honest: the client always knows what
operation produced the data, whether it succeeded, and why it failed.

---

## The engine lifecycle

```
BeforeAnalyze
  → Pre-Interceptors (from registry, filtered by AppliesTo)
  → Pre-Analyzers (local, from builder)
AfterAnalyze

BeforeValidate → ValidateFunc → AfterValidate → status = Validated

BeforeExecute → ExecuteFunc → mark parties Completed → AfterExecute

BeforeSubOperations → recursive ExecuteAsync for each sub → AfterSubOperations

BeforePostAnalyze → Post-Analyzers → Post-Interceptors → AfterPostAnalyze
→ PostValidateFunc

DetermineStatus:
  no subs or all succeeded → Completed
  some succeeded → PartiallyCompleted
  all failed → Failed

BeforeComplete/BeforeFail → AfterComplete/AfterFail
```

If any pre-analyzer or pre-interceptor fails with `IsBlocking = true`, the
engine skips Execute entirely and returns failure.

---

## The four-layer architecture

```
Layer 4 — Apps (pages, controllers, brand CSS)
  @inject AppStore → read state
  @inject ClientOpEngine → Entry.Create("x") → execute
  @inject ApiReader → GET only (no engine)

Layer 3 — Client OpEngine
  Pre: local analyzers
  Dispatch: HttpDispatchInterceptor (tag: client_dispatch)
  Post: StateBridgeInterceptor → interpreters → AppStore

Layer 2 — Server OpEngine
  Pre: interceptors (QuotaCheck, ScheduleGate)
  Execute: DB mutations via provider contracts
  Post: interceptors (AuditLogger, Notifier)
  Wire: OperationEnvelope<T>

Layer 1 — Core engine + abstractions
  OperationEngine, Wire, Interceptors, SharedKernel
```

---

## What the model covers and what it doesn't

**Covers naturally** (state changes between parties):
- Financial transactions (sales, refunds, transfers)
- Messaging (send, deliver, read receipts)
- Authentication (challenge, verify, revoke)
- Notifications (send, deliver, dismiss)
- CRUD operations (create = transfer from creator to entity)
- Status transitions (order: pending → accepted → ready → delivered)
- UI state mutations (theme change, language change, cart add/remove)

**Does not cover** (no parties, no state change):
- Pure computation (calculate average, sort list, format string)
- Structural transformations (parse JSON, convert image format)
- Read queries (search, filter, aggregate)

These are regular functions. They live inside execute bodies, inside
analyzers, inside interceptors — but they are not entries themselves.
The model acknowledges their existence without forcing them into the
entry shape.

---

## Relation to existing work

| Approach | Similarity | Difference |
|---|---|---|
| **REA Ontology** (McCarthy 1982) | Resources-Events-Agents: every economic event = resource exchange between agents | OAM generalizes beyond economic events; adds analyzers, interceptors, provider contracts |
| **Event Sourcing** | Every change recorded as immutable event | ES events are free-form; OAM entries have mandatory debit/credit structure |
| **CQRS** | Separate read/write paths | CQRS separates the path, not the shape. OAM defines the write shape |
| **Saga Pattern** | Compose distributed transactions with compensation | Sagas focus on rollback. OAM focuses on accrual/fulfillment |
| **Petri Nets** | Tokens flow between places via transitions | No balance requirement, no party structure |
| **Algebraic Effects** | Side effects declared and handled externally | Similar to interceptor injection, but at function level not operation level |
| **LM.NET / automation systems** | Visual composition of processing steps with pluggable algorithms | Similar composition model; OAM adds the double-entry constraint and tag-based injection |

---

## Algebraic structure (sketch)

The model has algebraic properties worth noting for future formalization:

- **Parties** form a set with identity
- **Entries** are a binary relation on parties with value and constraints
- **Reversal** is an involution: reverse(reverse(E)) = E
- **Identity entry**: value 0, same party on both sides — changes nothing
- **Composition**: sub-entries form a free monoid (tree structure)
- **Analyzers**: constraints that define a quotient — only valid entries pass
- **Interceptors**: natural transformations — operate on the entry structure
  while preserving its shape
- **Accounts**: fold (reduce) over the party stream with a filter predicate

This structure resembles a **constrained category** where objects are
parties, morphisms are entries, and analyzers define which morphisms are
admissible. Interceptors are functors that transform the category while
preserving its structure.
