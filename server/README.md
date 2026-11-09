server side REST API:

- Database API - Endpoint: `/db` - in server/database
- Profile API - Endpoint: `/acc` - in server/accounts
- Scheduling API - Enpoint: `/sched` - in server/scheduler
- Feedback API - Endpoint: `/fb` - in server/feedback
- Analytics API - Endpoint: `/adata` - in server/analyst


- Messaging API (Last keep this at the end, this is a pretty big API) - endpoint: `/chat/v1` - in server/chat
- Authentication API — `/auth/v1` provides HOST-JWT-protected provider code exchange and a separate admin-login route. See [`API.md`](API.md#authentication--authv1) and apply schema v3 before enabling database auth persistence.