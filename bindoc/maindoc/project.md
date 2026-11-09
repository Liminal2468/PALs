# Project context

Research background, proposal findings, proposed scope, methodology, references, and
open questions for PALs are recorded in [notes/projectnotes.md](notes/projectnotes.md).
Use that note as context when making future product or API decisions; distinguish
proposal statements from implementation decisions and unresolved research questions.

## Glossary

- **CLIENT:** A student, tutor, tutee, teacher, or administrator using the learning platform.
- **HOST:** The Mentos machine and Flask API. It is the only service that calls DBSERV and the privacy boundary for CLIENT-facing data.
- **DBSERV:** The database API service on apmain. It validates HOST-issued JWTs and manages database access; CLIENTs do not connect to it directly.