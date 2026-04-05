# BrassLedger Administration Guide

This guide covers the administrative side of the application: account hygiene, data location, publish practices, and source-control expectations.

## Security baseline

The current application baseline already assumes:

- authenticated access before accounting data is loaded
- hashed passwords for operator credentials
- protected sensitive fields at rest
- data-protection key storage for application cryptography
- security headers in the web application and API

Before using live confidential books in production, plan for more:

- stronger role enforcement
- audit logging
- externalized key management where appropriate
- operational backup and restore procedures
- release approval discipline

## Data handling

Use these rules consistently:

- the database is the source of truth for accounting data
- local SQLite data and key material belong in a writable application data directory and should stay out of Git
- `Storage:DataRoot` can be set explicitly if you need to control where local application data is stored
- `BrassLedger.Web/wwwroot` is the source location for committed static assets
- `artifacts` is a publish output folder and should be disposable

## Publishing

A clean publish process looks like this:

1. Build from source.
2. Run the relevant tests.
3. Publish for the target runtime identifier.
4. Smoke-test the published build.
5. Package the publish output for distribution.
6. Store the package in a release channel, not as ordinary source control content.

## Git guidance

Track:

- application source
- tests
- documentation
- authored report assets
- static web assets in source form

Ignore:

- `artifacts`
- `bin`
- `obj`
- local application data directories such as `App_Data`
- local IDE state

## Support checklist

When a user reports a problem:

1. identify the module and workflow affected
2. confirm whether the issue is data, configuration, or application behavior
3. run the related report before attempting a balancing correction
4. capture the environment and release details
5. update documentation if the fix changes operator behavior
