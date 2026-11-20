# Changelog

## [0.6.0](https://github.com/KauaVilasBoas/Normetry/compare/v0.5.0...v0.6.0) (2026-11-20)


### ⚠ BREAKING CHANGES

* **core:** drop roles from /me
* **core:** Keycloak realm roles no longer authorize anything; a user needs a profile in the core database.

### Features

* **ai:** add ai.tenant_documents under tenant RLS ([1afdfb1](https://github.com/KauaVilasBoas/Normetry/commit/1afdfb15fed534f2096c23deadaf69dd72f205f8)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **ai:** chat answers that cite the establishment's documents ([6339b15](https://github.com/KauaVilasBoas/Normetry/commit/6339b15ff967b7ef07fc5c7f88c4858abc0a06a5)), closes [#107](https://github.com/KauaVilasBoas/Normetry/issues/107)
* **ai:** consume document messages from RabbitMQ in the service process ([8599dd4](https://github.com/KauaVilasBoas/Normetry/commit/8599dd43e370937855293889e7291b0e5336b530)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **ai:** index tenant document versions from blob storage ([474be1f](https://github.com/KauaVilasBoas/Normetry/commit/474be1f9569f28150e53fdb7cd7d610976b45928)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **ai:** inspection agent with checkpoints, budget and queue consumer ([7237e1b](https://github.com/KauaVilasBoas/Normetry/commit/7237e1b5934887c79a177d70424d61092d59ebe6)), closes [#101](https://github.com/KauaVilasBoas/Normetry/issues/101)
* **ai:** inspection checklists for the three regulations ([bc56b83](https://github.com/KauaVilasBoas/Normetry/commit/bc56b839f46ab68e5ed4ed5e10bb9a964b2287f8))
* **ai:** inspection judge with verified citations ([4226021](https://github.com/KauaVilasBoas/Normetry/commit/42260215a842cef9a135e9fe6589dd817fed382c)), closes [#100](https://github.com/KauaVilasBoas/Normetry/issues/100)
* **ai:** parse tenant PDF, DOCX, TXT and CSV uploads ([a27f97a](https://github.com/KauaVilasBoas/Normetry/commit/a27f97a8517dd5baa611f0348c126388db1a5b05)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **ai:** search an establishment's documents ([bf3c5bc](https://github.com/KauaVilasBoas/Normetry/commit/bf3c5bc3ecb31ca71d67dd174d0af5b79c97a903)), closes [#98](https://github.com/KauaVilasBoas/Normetry/issues/98)
* **core:** add a transactional outbox published to RabbitMQ ([be11cde](https://github.com/KauaVilasBoas/Normetry/commit/be11cdec8b1ef527a547356c7a5c015916e36e20))
* **core:** add the profile and user management API ([6ce26cb](https://github.com/KauaVilasBoas/Normetry/commit/6ce26cbd236150793994f551a6a2249fb4b1a393)), closes [#82](https://github.com/KauaVilasBoas/Normetry/issues/82)
* **core:** add users, profiles and the permission catalog with RLS ([d21dbba](https://github.com/KauaVilasBoas/Normetry/commit/d21dbbaf74177fb03ef4faa921a4da2aea7e6a7f)), closes [#80](https://github.com/KauaVilasBoas/Normetry/issues/80)
* **core:** apply ingestion status events and time out stuck versions ([9f952b6](https://github.com/KauaVilasBoas/Normetry/commit/9f952b62deec12b3537de4faf1ab8e8ed12a46cd))
* **core:** authorize every action by permissions from the core database ([a42e0c1](https://github.com/KauaVilasBoas/Normetry/commit/a42e0c19706ede68c344b6ac3a6e6765dea39beb)), closes [#81](https://github.com/KauaVilasBoas/Normetry/issues/81)
* **core:** cap the versions a document holds ([1bf6bb6](https://github.com/KauaVilasBoas/Normetry/commit/1bf6bb672b84ccd2edf5702c9a622575e042b226))
* **core:** create the user row at first sign-in and read its permissions ([1860b6a](https://github.com/KauaVilasBoas/Normetry/commit/1860b6ab1658d754de2ec841451794ce08a1cd6d)), closes [#80](https://github.com/KauaVilasBoas/Normetry/issues/80)
* **core:** establishment overview query ([cb93b6a](https://github.com/KauaVilasBoas/Normetry/commit/cb93b6a98632b4ff5bb9460a22a3103dcc34c038)), closes [#112](https://github.com/KauaVilasBoas/Normetry/issues/112)
* **core:** inspections, findings and action items with RLS ([b09900e](https://github.com/KauaVilasBoas/Normetry/commit/b09900e1f8b31c04f1ad8211ffb2aaf9c6131eed)), closes [#102](https://github.com/KauaVilasBoas/Normetry/issues/102)
* **core:** optional expiry date on documents ([65663c8](https://github.com/KauaVilasBoas/Normetry/commit/65663c819c89e5e2d7fe9c950da7de8de1dafb91)), closes [#97](https://github.com/KauaVilasBoas/Normetry/issues/97)
* **core:** push document status changes over SignalR ([afa8f93](https://github.com/KauaVilasBoas/Normetry/commit/afa8f93f14d913c464312bb89579fab1fe597112))
* **core:** rate limiting per tenant and user ([cbff598](https://github.com/KauaVilasBoas/Normetry/commit/cbff5989212772bf3c108911edb6188d2407a3de)), closes [#110](https://github.com/KauaVilasBoas/Normetry/issues/110)
* **core:** review findings and action items, with a feedback export ([9e2a7a0](https://github.com/KauaVilasBoas/Normetry/commit/9e2a7a0e09a1e46644f887ed9f7a47d186d080ce))
* **core:** run RabbitMQ and Azurite from the AppHost ([c2a5f0c](https://github.com/KauaVilasBoas/Normetry/commit/c2a5f0c079df1935ac1155b0334a0ec27a2c5a27))
* **core:** seed the system profiles and the demo users ([951f6b1](https://github.com/KauaVilasBoas/Normetry/commit/951f6b14d132b6facf5422f113eb0891922825ef)), closes [#80](https://github.com/KauaVilasBoas/Normetry/issues/80)
* **core:** start inspections, apply their results and push live progress ([dd459ab](https://github.com/KauaVilasBoas/Normetry/commit/dd459abed8a03a03a612a47072a8d4f415d8fb9e)), closes [#102](https://github.com/KauaVilasBoas/Normetry/issues/102)
* **core:** upload, list, get and delete tenant documents ([279ab68](https://github.com/KauaVilasBoas/Normetry/commit/279ab684244205c9e24427860e0f347af8351038))
* **evals:** document questions and a security set for the chat ([80e355b](https://github.com/KauaVilasBoas/Normetry/commit/80e355b8ca081673f224081f6e73413c3e3ea285)), closes [#109](https://github.com/KauaVilasBoas/Normetry/issues/109)
* **evals:** inspection eval against the synthetic establishments ([8f1c832](https://github.com/KauaVilasBoas/Normetry/commit/8f1c832afb02de7f5c006b4a58b70a5ad46f62b9)), closes [#106](https://github.com/KauaVilasBoas/Normetry/issues/106)
* **evals:** score document questions and the chat security set ([13053fb](https://github.com/KauaVilasBoas/Normetry/commit/13053fb92226336453ad87db2789c54d237435b4)), closes [#109](https://github.com/KauaVilasBoas/Normetry/issues/109)
* **evals:** synthetic documents and ground truth for Bakery Austin ([293978f](https://github.com/KauaVilasBoas/Normetry/commit/293978f2fc4e52861b45e61263e25b4e492befc7)), closes [#96](https://github.com/KauaVilasBoas/Normetry/issues/96)
* **evals:** synthetic documents and ground truth for Bistro Lisboa ([4100c9e](https://github.com/KauaVilasBoas/Normetry/commit/4100c9e36e9aee0759dd4b69a730a28e574ad78e)), closes [#96](https://github.com/KauaVilasBoas/Normetry/issues/96)
* **evals:** synthetic documents and ground truth for Bistro São Paulo ([8b45bbe](https://github.com/KauaVilasBoas/Normetry/commit/8b45bbe00b83d5f09f5616fb4a1e2ba6eb6a6a05)), closes [#96](https://github.com/KauaVilasBoas/Normetry/issues/96)
* **shared:** inspection message contracts and document titles on uploads ([e961370](https://github.com/KauaVilasBoas/Normetry/commit/e9613707016666b53af61dbaed976bbd9125633d))
* **shared:** versioned queue message contracts for tenant documents ([0f71285](https://github.com/KauaVilasBoas/Normetry/commit/0f71285f6724f2abca1a0b2a3913ecfc7d18320e)), closes [#16](https://github.com/KauaVilasBoas/Normetry/issues/16)
* **web:** add the ingestion pill, drop zone, notice and select field primitives ([92775a6](https://github.com/KauaVilasBoas/Normetry/commit/92775a660c2665f23021b600766bd1486ab716ca))
* **web:** add the stepper, radial progress, checklist rows and status pills ([93af7e8](https://github.com/KauaVilasBoas/Normetry/commit/93af7e8ce0ca5f1062636da68563c48b6fc0a751))
* **web:** add the tab strip, tag, checkbox, text field and form dialog primitives ([e8ed116](https://github.com/KauaVilasBoas/Normetry/commit/e8ed116293daa50e38a1620ea20e84264b1e5832))
* **web:** document citations in the chat ([b78daf9](https://github.com/KauaVilasBoas/Normetry/commit/b78daf964b41167a3bda83f45e0d56c0708714a5)), closes [#108](https://github.com/KauaVilasBoas/Normetry/issues/108)
* **web:** documents screen with upload, versions and live status ([2d28c1a](https://github.com/KauaVilasBoas/Normetry/commit/2d28c1a609e76cf568e30ac0f98663bad8da7a63)), closes [#88](https://github.com/KauaVilasBoas/Normetry/issues/88)
* **web:** findings review and action plan ([b51db2f](https://github.com/KauaVilasBoas/Normetry/commit/b51db2f537a26d0324418a013b7194444d38ca03))
* **web:** hide what the user's permissions from /me do not allow ([8c80dee](https://github.com/KauaVilasBoas/Normetry/commit/8c80dee46b19f41a32395477045852ffcded9136))
* **web:** open with the remembered or the first establishment ([1065605](https://github.com/KauaVilasBoas/Normetry/commit/1065605a62681f8691fdcf4c2c5cdb0256b0b3b8))
* **web:** Overview screen for the selected establishment ([538dc6d](https://github.com/KauaVilasBoas/Normetry/commit/538dc6daebdb3934ecdcbd38371d648938702264)), closes [#112](https://github.com/KauaVilasBoas/Normetry/issues/112)
* **web:** profiles tab under settings ([a785a06](https://github.com/KauaVilasBoas/Normetry/commit/a785a0695883510facb1498dcc3e4378bcf1ef1d))
* **web:** show and filter document expiry dates ([429c092](https://github.com/KauaVilasBoas/Normetry/commit/429c0920646bc357d59f1dd5a49985f82c7bd9fb)), closes [#97](https://github.com/KauaVilasBoas/Normetry/issues/97)
* **web:** simulated inspection screen with live progress ([689c057](https://github.com/KauaVilasBoas/Normetry/commit/689c0575bfc8528a566575cbc559f1c5fb177fd7)), closes [#104](https://github.com/KauaVilasBoas/Normetry/issues/104)
* **web:** tell a user whose profile opens no screen ([88f4395](https://github.com/KauaVilasBoas/Normetry/commit/88f4395f7c3ef0af83e4bb8db141ccb5d85feafe))


### Bug Fixes

* **ai:** cap a tenant document version at 500 chunks ([686a548](https://github.com/KauaVilasBoas/Normetry/commit/686a548e82af07b19b1819e3017e6fffb17233cb)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **ai:** dead-letter a message whose type or version is a list or an object ([07b53da](https://github.com/KauaVilasBoas/Normetry/commit/07b53dafd684f78c20cbdcb4187f5674106d86a3))
* **ai:** derive result event ids from the tenant too ([8a795c7](https://github.com/KauaVilasBoas/Normetry/commit/8a795c7ebd5de08975bb24a8b11fc258d717427e)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **ai:** harden the inspection agent after its isolation review ([c51fd3e](https://github.com/KauaVilasBoas/Normetry/commit/c51fd3e84a7d7a241d4b33932dd4908fa62b741c))
* **ai:** keep document text from callers without documents.read ([2a798b4](https://github.com/KauaVilasBoas/Normetry/commit/2a798b49ea572fd817716c342aed3fb03fc44d4f)), closes [#107](https://github.com/KauaVilasBoas/Normetry/issues/107)
* **ai:** keep file names out of logs and tag consumer spans with the tenant ([8b2d988](https://github.com/KauaVilasBoas/Normetry/commit/8b2d988b70b8e60ded48336ac7a859406f6bc569)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **ai:** keep the parsing subprocess from reading the service's environment ([9bdcb5c](https://github.com/KauaVilasBoas/Normetry/commit/9bdcb5c43d6c20aba01afbcafd6ca2b76c6db2af)), closes [#90](https://github.com/KauaVilasBoas/Normetry/issues/90)
* **ai:** never report an indexed version as failed when only its result could not be sent ([87a457c](https://github.com/KauaVilasBoas/Normetry/commit/87a457c1eb21eab0435b85a69ea5d67ac857163d))
* **ai:** parse uploads in a credential-free subprocess with limits ([9760af4](https://github.com/KauaVilasBoas/Normetry/commit/9760af4d4faacd2507c62fd8af62b6df2e3da35b)), closes [#90](https://github.com/KauaVilasBoas/Normetry/issues/90)
* **ai:** put the tenant id on document worker and consumer log lines ([ec24538](https://github.com/KauaVilasBoas/Normetry/commit/ec24538efa9e42a7baf5d236997c7f1cf79f3d51)), closes [#86](https://github.com/KauaVilasBoas/Normetry/issues/86)
* **core:** answer 409 when two requests give profiles the same name ([1736076](https://github.com/KauaVilasBoas/Normetry/commit/1736076ae79514d4e5f594cd99cc5c31c00c3fa2))
* **core:** close the inspection cap's midnight race and time out lost requests ([500f815](https://github.com/KauaVilasBoas/Normetry/commit/500f815f5165a9692bfd5e3396f7efecf4450388))
* **core:** count text limits in characters, as the message contract does ([81c622e](https://github.com/KauaVilasBoas/Normetry/commit/81c622eaa51a4b1072804ee7e0aa11906d7b7d8d))
* **core:** expire inspection requests the core has already timed out ([999b1bf](https://github.com/KauaVilasBoas/Normetry/commit/999b1bfd7554d913b677d58e75978fa812aaea70))
* **core:** keep edited findings in the feedback export after an accept ([39f65ee](https://github.com/KauaVilasBoas/Normetry/commit/39f65eefcaea6bedcfeb46a9b6d67463f9bbc8a5))
* **core:** log the tenant id when a status delivery is retried ([eff4914](https://github.com/KauaVilasBoas/Normetry/commit/eff491472a77e64cc1cea7f316e4cc002ee7ce35))
* **core:** read the outbox tenant list only as core_dispatcher ([913cac1](https://github.com/KauaVilasBoas/Normetry/commit/913cac1ea5b8073f97dddc6f18ff4005c69d66e9))
* **core:** refuse a file name with an overlong extension instead of failing with 500 ([13e0172](https://github.com/KauaVilasBoas/Normetry/commit/13e01722b00f6ec19e5648f33443d17579deff1a))
* **core:** start the status consumer again when the broker cancels it ([f1221a9](https://github.com/KauaVilasBoas/Normetry/commit/f1221a9bfade812b3e933b56f9794d9320ba1fa8))
* **core:** tell the AI service whether the caller may read documents ([cec5f7d](https://github.com/KauaVilasBoas/Normetry/commit/cec5f7df917105d909bbe99e034fae02170ee4a2))
* **core:** tighten tenant handling in the background services and the hub ([1044ce5](https://github.com/KauaVilasBoas/Normetry/commit/1044ce58db748a06cb924dda0033e401fd4484ff))
* hold a message while the database or blob storage is down instead of dead-lettering it ([7fad802](https://github.com/KauaVilasBoas/Normetry/commit/7fad8028863e410909b1d90e9bf4fdab312d60f3))
* **infra:** give the AI service a read-only blob credential ([04624dd](https://github.com/KauaVilasBoas/Normetry/commit/04624dd135fd7c122fde583ae43afc4b29cfd4cc)), closes [#90](https://github.com/KauaVilasBoas/Normetry/issues/90)
* **infra:** keep PUBLIC from executing functions the schema owners create ([f7da13d](https://github.com/KauaVilasBoas/Normetry/commit/f7da13d27d0f07d12713fd2f4f5519b07d5d8477))
* retry a failed delivery for 102 seconds before dead-lettering it ([b59b8ba](https://github.com/KauaVilasBoas/Normetry/commit/b59b8ba43c92e82af3f12acfe1989a96f66e2907))
* **web:** apply the design review of the chat document citations ([04ae7e4](https://github.com/KauaVilasBoas/Normetry/commit/04ae7e46f4ebcc67dda440c0d0ffc743695c08e5))
* **web:** apply the design review of the document expiry dates ([2eecceb](https://github.com/KauaVilasBoas/Normetry/commit/2eeccebd87a57dcd26c32e6d77be1c1640f96648)), closes [#97](https://github.com/KauaVilasBoas/Normetry/issues/97)
* **web:** apply the design review of the findings review ([fb97136](https://github.com/KauaVilasBoas/Normetry/commit/fb971362502b3ec9f3dfc33febdf0b9c3675c3f9)), closes [#105](https://github.com/KauaVilasBoas/Normetry/issues/105)
* **web:** apply the design review of the inspection screen ([4a0787b](https://github.com/KauaVilasBoas/Normetry/commit/4a0787be02a995b908da548f9e3604cebd028da4)), closes [#104](https://github.com/KauaVilasBoas/Normetry/issues/104)
* **web:** apply the design review of the Overview ([7766a30](https://github.com/KauaVilasBoas/Normetry/commit/7766a303552a3ec4e6d2623fafaafbf6a9dfe320)), closes [#112](https://github.com/KauaVilasBoas/Normetry/issues/112)
* **web:** divide the account menu with --bg-inset ([dc8952f](https://github.com/KauaVilasBoas/Normetry/commit/dc8952f543fec24db390845f576b5079bd0f2453))
* **web:** keep a refusal in its dialog or menu until it closes ([fa8fe28](https://github.com/KauaVilasBoas/Normetry/commit/fa8fe2858a92a27d6bc41a2c3bf949c7f984c5ef))
* **web:** keep the focus ring of the settings tab and back link visible ([dec2395](https://github.com/KauaVilasBoas/Normetry/commit/dec23959ed3a8cfdda97f293e649ab7df8ec6a93))
* **web:** keep the profiles screen when a background /me refetch fails ([c4e99a0](https://github.com/KauaVilasBoas/Normetry/commit/c4e99a02813196b83e68a58f1cac90ac340574ad))
* **web:** keep the upload dialog and the live status heard and in place ([d2ac181](https://github.com/KauaVilasBoas/Normetry/commit/d2ac181c6687e305e15dd235d12c392b3ad60c2a))
* **web:** label the nav skeleton and sort the account menu's profiles ([83aa6f1](https://github.com/KauaVilasBoas/Normetry/commit/83aa6f16c5d4ed9c5bc0e047230bd3632b53fc64))
* **web:** move focus to a heading when a lost permission removes the focused control ([15fd03c](https://github.com/KauaVilasBoas/Normetry/commit/15fd03c06955504398c28d08db03fbee8f3b1ca5))
* **web:** put a user's Profiles button on the tag line below 640px ([9adcb99](https://github.com/KauaVilasBoas/Normetry/commit/9adcb992bc5a2bef70815566b2192e42a6e06a81))
* **web:** send the content type the core expects for the file's extension ([b06fda6](https://github.com/KauaVilasBoas/Normetry/commit/b06fda6677a5749348674b2607b5511c779afd8e))
* **web:** show the error icon, and the error in the heading, in the profile editor ([5ae04ef](https://github.com/KauaVilasBoas/Normetry/commit/5ae04eff397eebeb9696eb9d7e185a4b8c12c4ea))


### Performance Improvements

* **core:** count the inspection list's verdicts in SQL again ([835dbed](https://github.com/KauaVilasBoas/Normetry/commit/835dbede67a6839bf73a80bdfee5d1e7aa580b4c)), closes [#119](https://github.com/KauaVilasBoas/Normetry/issues/119)
* **core:** wake the outbox dispatcher when messages commit instead of polling ([8fef0b8](https://github.com/KauaVilasBoas/Normetry/commit/8fef0b8e69a5bc2c4bb8c5b260057b35ad7ce96a))


### Code Refactoring

* **core:** drop roles from /me ([ac1e235](https://github.com/KauaVilasBoas/Normetry/commit/ac1e23554e2516e176081739a8d7138fafa08cda))

## [0.5.0](https://github.com/KauaVilasBoas/Normetry/compare/v0.4.0...v0.5.0) (2026-11-15)


### Features

* **core:** serve the login theme from the AppHost's Keycloak ([8e418fa](https://github.com/KauaVilasBoas/Normetry/commit/8e418fa0779c44cbd362b0e1151a3100c2408ac8))
* **infra:** add the Normetry login theme for Keycloak ([53d53bd](https://github.com/KauaVilasBoas/Normetry/commit/53d53bd3e0b0262b0aff11ed97e6eb8f27987444))


### Bug Fixes

* **infra:** announce login field errors and the signing-in state ([00e28e5](https://github.com/KauaVilasBoas/Normetry/commit/00e28e5f04b8ddca76cac474498a401def3a7767))
* **infra:** draw the login wordmark with the SPA's Logo geometry ([635ce21](https://github.com/KauaVilasBoas/Normetry/commit/635ce211f517217f0e236bf3f8fba0a7343c9b1a))
* **infra:** drop the uppercase eyebrow from the login introduction ([3ee1b46](https://github.com/KauaVilasBoas/Normetry/commit/3ee1b469b5331492d3c13f903dddb45b575410ae))
* **infra:** fit the password toggle inside its field ([a15d9a6](https://github.com/KauaVilasBoas/Normetry/commit/a15d9a621f6c45acc5af50c56d017e7ccb490aa3))
* **infra:** fix login landmarks, reading order and the locale label ([0c5e949](https://github.com/KauaVilasBoas/Normetry/commit/0c5e94937187f8de7c6f61e680a8b424298270fa))
* **infra:** give locale menu items and checkbox labels 44px targets ([6116b4d](https://github.com/KauaVilasBoas/Normetry/commit/6116b4d5f16101c8d2d818f7ac9138d3e9981af3))
* **infra:** keep secondary and disabled login buttons out of the primary style ([7ab99db](https://github.com/KauaVilasBoas/Normetry/commit/7ab99db99e908017a5cbae04afd7d99f86445584))
* **infra:** mark login errors with an alert icon at body size ([7ff89ad](https://github.com/KauaVilasBoas/Normetry/commit/7ff89ad9e386669fab64ae46a04a7e303c1f960f))
* **infra:** match the login title and hero type to the design tokens ([3c5c89a](https://github.com/KauaVilasBoas/Normetry/commit/3c5c89a3faa6d722ecf1326532f8d48250d9cbdb))
* **infra:** show the login tagline in one casing and at body size ([97db669](https://github.com/KauaVilasBoas/Normetry/commit/97db6694efd12f86c3ea411fe9320a4baa264b5c))
* **infra:** space paragraphs and hide the empty info box on login pages ([c575a40](https://github.com/KauaVilasBoas/Normetry/commit/c575a403929d2a951cc3c96b79c51dab0913fa8a))
* **infra:** underline links in login instruction text ([90c06b6](https://github.com/KauaVilasBoas/Normetry/commit/90c06b6ca5e30ec57fe5bfb845957abb33d3e697))
* **infra:** use the icon-tile radius and drop unused login theme tokens ([72a5517](https://github.com/KauaVilasBoas/Normetry/commit/72a55172b219e99096ca3c1c4c05fd4df8f5e72b))
* **infra:** use the SPA's line heights and focus offset in the login theme ([89ff552](https://github.com/KauaVilasBoas/Normetry/commit/89ff552628bb3d6b643521fd57fc83ba1130eda2))
* **web:** give the chat input and establishment switcher a teal border ([ab39618](https://github.com/KauaVilasBoas/Normetry/commit/ab39618bc35bee44160b7b52361d1f828234713d))

## [0.4.0](https://github.com/KauaVilasBoas/Normetry/compare/v0.3.0...v0.4.0) (2026-11-15)


### Features

* **ai:** run and score the generation eval per chat model ([9c54272](https://github.com/KauaVilasBoas/Normetry/commit/9c54272b66f6b0da60c1195d7eda18f62e521024))


### Bug Fixes

* **ai:** answer claims keep their conditions and never describe what the excerpts lack ([10f1da4](https://github.com/KauaVilasBoas/Normetry/commit/10f1da479996bb22ef11cfe48f4eb23b0c62875d))
* **ai:** rewrite follow-ups through structured output ([e0889df](https://github.com/KauaVilasBoas/Normetry/commit/e0889df65e0b78d9eade719326ebe0d2ffbc4327))

## [0.3.0](https://github.com/KauaVilasBoas/Normetry/compare/v0.2.0...v0.3.0) (2026-11-14)


### Features

* **ai:** add conversations and messages under RLS ([047cf00](https://github.com/KauaVilasBoas/Normetry/commit/047cf00b1efb9a438c51621dc839d93ae44d37a8))
* **ai:** add the bge-reranker-v2-m3 re-ranker and retrieval settings ([228cd71](https://github.com/KauaVilasBoas/Normetry/commit/228cd71bb101d1cfa3e85ea9306548e24da0071d))
* **ai:** add the retrieved chunk type shared by retrieval and answers ([9559541](https://github.com/KauaVilasBoas/Normetry/commit/9559541cd76a323fb093b92e9fb4956cc1ca6f84))
* **ai:** add the text normalization shared with the quote check ([4066b0e](https://github.com/KauaVilasBoas/Normetry/commit/4066b0e95d688ebbdb748ca0ab956d872a1d0a54))
* **ai:** answer chat turns from hybrid retrieval ([c35ba7e](https://github.com/KauaVilasBoas/Normetry/commit/c35ba7e42f10e7c00ee743ee75982b310b704488))
* **ai:** answer with quoted claims behind a citation guard ([c8e7bd8](https://github.com/KauaVilasBoas/Normetry/commit/c8e7bd85f4520fef14d8914c0e3320d50b6f1bf9))
* **ai:** cut corpus sections into size-limited chunks ([56eab9e](https://github.com/KauaVilasBoas/Normetry/commit/56eab9ea8428039993107adb595f2bb68f892091))
* **ai:** embed queries through the embedding interface ([64f3218](https://github.com/KauaVilasBoas/Normetry/commit/64f321841d5e8ff6b9f233f33a6dceada559011a))
* **ai:** embed text with BGE-M3 on CPU behind the embedding interface ([72ba701](https://github.com/KauaVilasBoas/Normetry/commit/72ba701b13b787626bc7a2de6de633ed348f1fa0))
* **ai:** hybrid retrieval with RRF, re-ranking and a jurisdiction policy ([7bc7227](https://github.com/KauaVilasBoas/Normetry/commit/7bc722763b482f8230f68bb67b198f50fc984bdc))
* **ai:** ingest the corpus into ai.sources and ai.chunks ([4139c48](https://github.com/KauaVilasBoas/Normetry/commit/4139c480a2f7d5919ed59c72cf7cf3b70b883248))
* **ai:** keep the HNSW index to corpus chunks ([df77fed](https://github.com/KauaVilasBoas/Normetry/commit/df77fed54dc028a407cc6781052323d9cba0ea57))
* **ai:** parse the three regulations into citable sections ([f8882dd](https://github.com/KauaVilasBoas/Normetry/commit/f8882dd81152b3321a218746f737cf4bf1d689bc))
* **ai:** read the establishment headers on establishment routes ([8643e0f](https://github.com/KauaVilasBoas/Normetry/commit/8643e0f0468be02f16c7977c92247d5fafa5f889))
* **ai:** record model tokens and cost per tenant and user ([3b9ac3e](https://github.com/KauaVilasBoas/Normetry/commit/3b9ac3e3dbb324a7e88ddc87abd551fddb49d2fe))
* **ai:** record the usage so far when an answer stops early ([ef9df92](https://github.com/KauaVilasBoas/Normetry/commit/ef9df928bc640bf5beebbe60e823122fbb5c585d))
* **ai:** rewrite follow-ups into standalone questions ([558fc9d](https://github.com/KauaVilasBoas/Normetry/commit/558fc9d14293c53d2d0569985e0a6c94dc55316b))
* **ai:** score retrieval against the golden dataset ([2b998ec](https://github.com/KauaVilasBoas/Normetry/commit/2b998ece66bc7142128237711da99db364b0570d))
* **ai:** send the answer language with each streamed claim ([d3326a9](https://github.com/KauaVilasBoas/Normetry/commit/d3326a9c234f7018148359908e51a9285333469f))
* **ai:** serve conversations and stream guarded claims over SSE ([b1b8c60](https://github.com/KauaVilasBoas/Normetry/commit/b1b8c606f777679d65acc8a31c8f4eb6af463409))
* **core:** add daily chat message counts under RLS ([e8b0c3f](https://github.com/KauaVilasBoas/Normetry/commit/e8b0c3f59fb90f4ec4e0529ffbbacd187b534394))
* **core:** daily chat quota by plan ([5837228](https://github.com/KauaVilasBoas/Normetry/commit/583722846986be339d020dbd64aa270919454fa3))
* **core:** establishment-scoped chat routes in the AI gateway ([e1b440a](https://github.com/KauaVilasBoas/Normetry/commit/e1b440abb82c945404af2a2cf83898474fb93df6))
* **core:** return the tenant name from /me ([0f95da1](https://github.com/KauaVilasBoas/Normetry/commit/0f95da1235529ebaf84476ae7d5707c6ff72c7c1))
* **core:** seed demo establishments in each jurisdiction ([ddc2565](https://github.com/KauaVilasBoas/Normetry/commit/ddc2565f28cc0ba750b7509059f0fa415d814576))
* **evals:** add the regulation QA golden dataset ([0430dbe](https://github.com/KauaVilasBoas/Normetry/commit/0430dbe985f5b655ec38a852e36d5cd470432ee2))
* **infra:** add the normetry-web Keycloak client ([198f385](https://github.com/KauaVilasBoas/Normetry/commit/198f385223344e771fa1703cf56a083b9b21406f))
* **infra:** pass the Anthropic API key to the AI service ([5e84ccf](https://github.com/KauaVilasBoas/Normetry/commit/5e84ccf1b2d8bdea6cc3563a15474707a62c0fc1))
* **infra:** run corpus ingestion on demand from the AppHost ([5bceeb5](https://github.com/KauaVilasBoas/Normetry/commit/5bceeb563485cf8cc539476503d009787659a1de))
* **infra:** run the web app from the AppHost ([80c9218](https://github.com/KauaVilasBoas/Normetry/commit/80c92187ffff078f9fa64f86fa36d9591041b305))
* **web:** add scroll buttons to the establishment switcher list ([a6c7fe9](https://github.com/KauaVilasBoas/Normetry/commit/a6c7fe94c48391766e4ad9cb36c8d489eaa05fe9))
* **web:** chat screen with streamed claims, citations and source panel ([fcb585c](https://github.com/KauaVilasBoas/Normetry/commit/fcb585c1c02d916740575c5558be63285bebac8b))
* **web:** scaffold the web app with Keycloak sign-in and establishment picker ([12196b4](https://github.com/KauaVilasBoas/Normetry/commit/12196b4113ff547a4ee2eec2cb24dcf9262525ad))


### Bug Fixes

* **ai:** filter by question language only where a corpus has two versions ([2a92066](https://github.com/KauaVilasBoas/Normetry/commit/2a9206652a7177e0e94c09ecc6860233855f7552))
* **ai:** keep tenant text out of database error logs ([bc0bd20](https://github.com/KauaVilasBoas/Normetry/commit/bc0bd20741c568448d9acc4710978086b0c9c279))
* **ai:** load each local model once when first questions overlap ([490b638](https://github.com/KauaVilasBoas/Normetry/commit/490b6382c01f231cee8b258b0312063807a0194b))
* **ai:** refuse on the re-ranker score only when retrieval finds nothing ([5d2d8b5](https://github.com/KauaVilasBoas/Normetry/commit/5d2d8b5a9bb50256d70e009db24deb274b451b72))
* **ai:** store an answer in the language of its question ([eadbc57](https://github.com/KauaVilasBoas/Normetry/commit/eadbc57e8f29f2cd68c3cfdfcfa996cc795e4d6d))
* **ai:** store an answer only in the user's own conversation ([1ce891f](https://github.com/KauaVilasBoas/Normetry/commit/1ce891fded49274710385da4b8f829917bd28805))
* **core:** keep establishment paths off the catch-all AI route ([e1295e5](https://github.com/KauaVilasBoas/Normetry/commit/e1295e59adfc1775b790397651a2042c42987ad1))
* **core:** refuse encoded slashes and empty segments on establishment routes ([1a68cd5](https://github.com/KauaVilasBoas/Normetry/commit/1a68cd5e1e53a428705b4c69d0f0116203408a08))
* **corpus:** record the current hash of the RDC 216/2004 page ([80ece7e](https://github.com/KauaVilasBoas/Normetry/commit/80ece7e17f2ec417dc6be9ff1625fded3fa34f90))
* **infra:** keep Keycloak, the AI service and the web app on plain http ([aa7fdba](https://github.com/KauaVilasBoas/Normetry/commit/aa7fdba9a5e9313a956200b68f231b7059ec22bc))
* **web:** add a close button to the conversations sheet ([3338cfe](https://github.com/KauaVilasBoas/Normetry/commit/3338cfe8742b8e0b9e43940809a0b313eb087c13))
* **web:** announce a conversation that is gone and focus the way out ([c5df329](https://github.com/KauaVilasBoas/Normetry/commit/c5df32998916f44d4ce4b93e1a7ee505e225254c))
* **web:** close the drawer when the window widens to the desktop layout ([fdc8070](https://github.com/KauaVilasBoas/Normetry/commit/fdc8070e9fb395b9fe67a0da9c56eb00e66e5666))
* **web:** describe establishment options with their type and jurisdiction ([f8791bd](https://github.com/KauaVilasBoas/Normetry/commit/f8791bdf2c371985fdfd4b6046c7d16d04fce477))
* **web:** focus the conversation heading when the sheet closes on a wider window ([cfa711a](https://github.com/KauaVilasBoas/Normetry/commit/cfa711a759e0544cdcf34450a9730dd033875653))
* **web:** forget the selected establishment on sign-out ([ea50f76](https://github.com/KauaVilasBoas/Normetry/commit/ea50f76a1d171ff9677fa9ddf45885a00ba8ccb7))
* **web:** give streamed answers their language ([d79b974](https://github.com/KauaVilasBoas/Normetry/commit/d79b9746524e6de0faffa84f3b9eeed6450fa2fa))
* **web:** keep focus and show progress when retrying the establishments ([aed02a6](https://github.com/KauaVilasBoas/Normetry/commit/aed02a66dc7e5ca0ea8c7bb1ae3b41001acca6fb))
* **web:** keep focus in the composer when Try again retries an answer ([3c2db33](https://github.com/KauaVilasBoas/Normetry/commit/3c2db336a4754f3e33dbec4e34c2cc3e6a9f4e89))
* **web:** keep muted text off the hovered user card ([7829eaf](https://github.com/KauaVilasBoas/Normetry/commit/7829eaf419a1ed12d80d5f46eb3b8b39e80ca664))
* **web:** keep the spinner and the divider visible on elevated and mint surfaces ([c9aad37](https://github.com/KauaVilasBoas/Normetry/commit/c9aad371202eed50d975083c7837a6386b813b58))
* **web:** move focus to the heading after Try again reloads ([09329e4](https://github.com/KauaVilasBoas/Normetry/commit/09329e46e89ede651f06455e97ace6672205fa69))
* **web:** name the establishment switcher "Establishment" ([4502650](https://github.com/KauaVilasBoas/Normetry/commit/4502650fc75b66a996aebf5ecbe75b2624336b3a))
* **web:** reject return paths whose dot segments resolve to another host ([55f2ab5](https://github.com/KauaVilasBoas/Normetry/commit/55f2ab589f218588c19413f77142c7033fce4bc0))
* **web:** require the OIDC authority outside development ([7b3a330](https://github.com/KauaVilasBoas/Normetry/commit/7b3a33018172e11d9ad0dbff28f0cb0eda83fdb3))
* **web:** say "Loading conversation…" once ([cce88fc](https://github.com/KauaVilasBoas/Normetry/commit/cce88fc3cf933a8ab9c83677a5d8b5e0d79e0da0))
* **web:** say "Signing you in…" while restoring the session ([014ce46](https://github.com/KauaVilasBoas/Normetry/commit/014ce46abb5453d8a3b3d99c2f4f039f83aa3783))
* **web:** show the email once when the profile has no name ([7883cb7](https://github.com/KauaVilasBoas/Normetry/commit/7883cb7c6fb13f2c8e2578187c254150a722b33e))
* **web:** show the picker until both requests succeed ([7d43d49](https://github.com/KauaVilasBoas/Normetry/commit/7d43d4921225585e91aecf4c3fa434fc43d9af4f))
* **web:** show the unavailable Delete and the open chip without color alone ([2404512](https://github.com/KauaVilasBoas/Normetry/commit/240451243e295cf0ec82e0505e4ee58ee2169513))

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
