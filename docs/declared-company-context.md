# Declared company context

CompanyService owns explicit capture of operational company declarations. ContextService mirrors the additive `CompanyProfile.declaredProfile` contract on its existing internal knowledge proxy; it does not capture current operational fields during reads or alter the requested `asOf`.

`declaredProfile` contains nullable `region`, nullable `industrySections` (A–U, separate from numeric `industryCodes`), nullable `revenue` (`amount` is an exact decimal string, unknown `currency` and `period` remain null), nullable `countOfEmployees`, and exact immutable snapshot `sourceRefs`. The enclosing knowledge revision supplies `revisionId` and `knownAt`. An omitted legacy block reads as null and remains omitted when serialized, preserving the legacy immutable payload shape. Explicit null members inside a present block remain null; an explicit employee count of zero remains zero.

The capture source uses `sourceId=company-operational-declared-profile`. It is a declared relevance signal, not a contract, financial exposure, budget, or payment-derived fact. This change adds no public routes or operational fields to the public material-list DTO. Existing CompanyService source identity, company scope, immutable revision, and knowledge-time checks remain authoritative.
