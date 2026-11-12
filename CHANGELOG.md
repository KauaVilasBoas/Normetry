# Changelog

## [0.2.0](https://github.com/KauaVilasBoas/Normetry/compare/v0.1.0...v0.2.0) (2026-11-12)


### Features

* **ai:** add OpenTelemetry tracing for the AI service ([09d9a5c](https://github.com/KauaVilasBoas/Normetry/commit/09d9a5c9fd0767c60df2483e6b60c9512cdbb016)), closes [#8](https://github.com/KauaVilasBoas/Normetry/issues/8)
* **ai:** add tenant unit of work and runtime role check ([db8d214](https://github.com/KauaVilasBoas/Normetry/commit/db8d2144df5ec9fdd00a1d06334de7a841f05cd8)), closes [#13](https://github.com/KauaVilasBoas/Normetry/issues/13)
* **ai:** close the AI service with an internal-token middleware ([6ec27c0](https://github.com/KauaVilasBoas/Normetry/commit/6ec27c0b73fadae53df9077c21d5f0e7aab0ed81)), closes [#8](https://github.com/KauaVilasBoas/Normetry/issues/8)
* **ai:** create ai.chunks with forced RLS and role-scoped policies ([c0d7e41](https://github.com/KauaVilasBoas/Normetry/commit/c0d7e4116eae7b0e57d7c46f58caea00c1cfe21b)), closes [#13](https://github.com/KauaVilasBoas/Normetry/issues/13)
* **ai:** read tenant and user from the internal headers ([796a2d0](https://github.com/KauaVilasBoas/Normetry/commit/796a2d00e010bc2ad7168a0b419097f43c4959a7))
* **ai:** revoke app_ai access to the Alembic version table ([cb14588](https://github.com/KauaVilasBoas/Normetry/commit/cb145880a06c9bf5f120134720a480f6ce96c2ca)), closes [#13](https://github.com/KauaVilasBoas/Normetry/issues/13)
* **core:** add current user and tenant context abstractions ([5fabbdb](https://github.com/KauaVilasBoas/Normetry/commit/5fabbdb9f1fbe57ee3f06ac8cd081e686bd8e796)), closes [#11](https://github.com/KauaVilasBoas/Normetry/issues/11)
* **core:** add establishment commands and queries ([4c13448](https://github.com/KauaVilasBoas/Normetry/commit/4c134483d9cbf3fc4d96e78b5126a6fe925a3e89))
* **core:** add establishment endpoints and the OwnerOrManager policy ([ff58eb5](https://github.com/KauaVilasBoas/Normetry/commit/ff58eb5664d4544b3d03e41ae0da5115acb30d7f))
* **core:** add the GetCurrentUser query ([e6e9572](https://github.com/KauaVilasBoas/Normetry/commit/e6e9572b380645b0f0bad2d404af3a49229daeb7)), closes [#11](https://github.com/KauaVilasBoas/Normetry/issues/11)
* **core:** add the tenant and establishment domain model ([105da04](https://github.com/KauaVilasBoas/Normetry/commit/105da04109a97853100c96c901990d5bc5620adb))
* **core:** authenticate Keycloak tokens and resolve the tenant from the JWT ([fdc9023](https://github.com/KauaVilasBoas/Normetry/commit/fdc9023c4e31eabee285020e4466c2894ed4a3d7)), closes [#11](https://github.com/KauaVilasBoas/Normetry/issues/11)
* **core:** create core tenants and establishments with forced RLS ([c33448b](https://github.com/KauaVilasBoas/Normetry/commit/c33448b345a027d8b17443533f14516b3d98bb1b))
* **core:** forward /ai/* to the AI service through YARP ([18bb271](https://github.com/KauaVilasBoas/Normetry/commit/18bb2717cdb67948028dbdfa96ce0ff5c41b6162))
* **core:** scope every core transaction to the request tenant ([fb38c2d](https://github.com/KauaVilasBoas/Normetry/commit/fb38c2d54fd06a1560398c0fbe9724db31dd92c1))
* **corpus:** add idempotent fetch script with hash verification ([0ce0b54](https://github.com/KauaVilasBoas/Normetry/commit/0ce0b544139518112cfb86dfe5fd669082fbf698)), closes [#15](https://github.com/KauaVilasBoas/Normetry/issues/15)
* **corpus:** add manifest of the three regulations ([3f51649](https://github.com/KauaVilasBoas/Normetry/commit/3f51649588c3b7ea92ea466b0c75a196d44d4e4e)), closes [#15](https://github.com/KauaVilasBoas/Normetry/issues/15)
* **infra:** add Keycloak realm with demo tenants and roles ([1d5c04f](https://github.com/KauaVilasBoas/Normetry/commit/1d5c04f1c64c51c29519acb5f0f5f273cd4eabab)), closes [#9](https://github.com/KauaVilasBoas/Normetry/issues/9)
* **infra:** bootstrap PostgreSQL roles and schemas ([0f89369](https://github.com/KauaVilasBoas/Normetry/commit/0f89369138ea85c7187222df14a3dd22d4a42a59)), closes [#9](https://github.com/KauaVilasBoas/Normetry/issues/9)
* **infra:** orchestrate PostgreSQL, Keycloak, API and AI service in the AppHost ([55f1979](https://github.com/KauaVilasBoas/Normetry/commit/55f19792501af4c5d79aa94b1ec742762639e08b)), closes [#9](https://github.com/KauaVilasBoas/Normetry/issues/9)
* **infra:** run ai migrations as a separate AppHost step ([698cecf](https://github.com/KauaVilasBoas/Normetry/commit/698cecfd77c893c49a4ef95363074d8edebaa69a)), closes [#13](https://github.com/KauaVilasBoas/Normetry/issues/13)
* **infra:** run core migrations as a separate AppHost step ([bc5d626](https://github.com/KauaVilasBoas/Normetry/commit/bc5d626f0b0436a8ca2b567ae913d2832b836994))

## 0.1.0 (2026-11-12)


### Features

* **core:** add in-house CQRS dispatcher with logging and validation behaviors ([a10641b](https://github.com/KauaVilasBoas/Normetry/commit/a10641b2e5e1f01566462d918c7dfe457d166571)), closes [#7](https://github.com/KauaVilasBoas/Normetry/issues/7)
* **core:** add Result and Error types for expected failures ([fee9a08](https://github.com/KauaVilasBoas/Normetry/commit/fee9a08947c4333da40902d6a216418efe65de79)), closes [#7](https://github.com/KauaVilasBoas/Normetry/issues/7)
* **core:** in-house CQRS dispatcher in Normetry.BuildingBlocks ([eef5a42](https://github.com/KauaVilasBoas/Normetry/commit/eef5a424792342e019cac7309feb2b6d12883046))
* **core:** map failed results to ProblemDetails in the API ([28bfe69](https://github.com/KauaVilasBoas/Normetry/commit/28bfe69df5f6d494ad8fcb627422844928a91320)), closes [#7](https://github.com/KauaVilasBoas/Normetry/issues/7)
