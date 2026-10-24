# Spec 453 — characterisation baseline (phase 4a, behaviour-preserving)

Captured on branch `refactor/3133-route-group-binding` at `fa57f8a0`, **before any code edit**, Release configuration.

## 1. Safety-net run (green)

```
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~RouteTableCharacterisationTests"
```

```
Passed!  - Failed:     0, Passed:   214, Skipped:     0, Total:   214, Duration: 5 s - SmartSentinelEye.Architecture.Tests.dll (net10.0)
```

Per class (from the TRX):

| Class | Tests |
|---|---|
| EndpointScopeDeclarationTests | 122 |
| StatusProducerDeclarationTests | 46 |
| RouteValueRefusalDeclarationTests | 21 |
| ConcurrencyConflictDeclarationTests | 13 |
| 9 `*RouteTableCharacterisationTests` classes (Audit 1, Automation 2, CameraCatalog 1, EventIngestion 2, Identity 2, LayoutComposition 1, OverlayDesigner 1, StreamDistribution 1, SystemVariables 1) | 12 |
| **Total** | **214** |

After the refactor the same command must print the same summary and the sorted `ClassName :: testName` list must be identical to
the baseline list (214 lines, kept at `C:\Users\heiko\AppData\Local\Temp\claude\D--Github-smart-sentinel-eye\f5a3a8f6-ef87-4789-82f0-c15d77998674\scratchpad\testnames-baseline.txt`).

## 2. Comment-stripped member hashes

Script (scratchpad, deliberately not in the repo): `C:\Users\heiko\AppData\Local\Temp\claude\D--Github-smart-sentinel-eye\f5a3a8f6-ef87-4789-82f0-c15d77998674\scratchpad\member-hashes.js`

```sh
cd tests/Architecture.Tests
node <scratchpad>/member-hashes.js EndpointScopeDeclarationTests.cs StatusProducerDeclarationTests.cs > after.txt
diff after.txt <scratchpad>/hashes-baseline.txt        # or against the table below
```

It strips `//` and `/* */` comments (string-aware: regular, verbatim, interpolated and raw strings are preserved), collapses code
whitespace, splits the outermost class body into members (methods, fields, regexes, `InlineData`/fixture constants, nested records)
and prints `sha256[:16]  TEST|other  memberKey`. `TEST` = a `[Fact]`/`[Theory]` member; its hash covers the attributes, so every
`InlineData`/`MemberData` block is inside it. Duplicate keys (overloads) get `#2`.

Expected diff after the refactor: **no `TEST` row changes, appears or disappears.** Permitted `other` changes are only the members
plan.md lists as deleted or edited (ES: `SharedSourceScanFiles` gains the one scan-list entry, `RouteBuilderNames`, `Read`,
`ReadMapping`; ES/SP binder helpers deleted; SP `GroupScopes`/`Mappings`). Anything else, or any `TEST` row, is a block, not a fix.

Counts at baseline: EndpointScopeDeclarationTests.cs 132 members (36 TEST); StatusProducerDeclarationTests.cs 68 members (23 TEST).
File digests: ES `0f8120c2d64a1a097d0e960ade271bf5cfc98c5a1e029c0acf1d7ce0ba4571f3`, SP `b9aa19a48186810f67a5c5d55220cb52c80a5cad3a3b7036f34b24d1c8a9cf30`.

```
# EndpointScopeDeclarationTests.cs members=132 tests=36
0135c247c796b0ec  TEST  A_generic_helper_whose_parameter_is_a_type_parameter_is_never_bound_to_the_group_of_the_same_name()
07ae72dda3acf8d8  TEST  A_helper_called_with_a_group_other_than_its_parameters_name_takes_the_called_groups_route_prefix()
050203caaf9caae5  TEST  A_helper_called_with_a_group_other_than_the_one_its_parameter_is_named_after_inherits_the_called_groups_scope()
75e16a5f325678ab  TEST  A_helper_called_with_an_argument_that_is_not_a_group_declared_in_the_file_is_unreadable_and_names_the_argument()
4ae2bcbb33d26e72  TEST  A_helper_called_with_the_group_its_parameter_is_named_after_inherits_that_groups_scope()
28da20bf61b37c0d  TEST  A_helper_called_with_two_different_groups_is_unreadable_and_names_the_helper_and_both_groups()
416eb4733b85059f  TEST  A_helper_declaring_its_own_scope_and_called_with_one_group_takes_that_groups_prefix()
734151c17fb34a4a  TEST  A_helper_declaring_its_own_scope_still_reports_a_group_it_cannot_establish()
df3bbf533b59d726  TEST  A_helper_never_called_in_its_file_is_unreadable_rather_than_bound_to_the_group_named_like_its_parameter()
3ae544abbc5ffad1  TEST  A_helper_whose_parameter_is_typed_by_a_using_alias_is_never_bound_to_the_group_of_the_same_name()
4b3cac70fb757199  TEST  A_parameter_the_reader_does_not_resolve_is_never_bound_to_the_group_local_of_the_same_name()
b0b652c99237ab9c  TEST  A_planted_unmaskable_form_is_reported_with_its_file_and_line()
7bba0078aaa25c32  TEST  A_raw_string_in_a_chain_runs_one_mapping_into_the_next()
b14ba37d73c7f10d  TEST  A_statement_bodied_lambda_does_not_end_the_chain_before_its_own_authorization()
f2c44e4e3d591bc9  TEST  A_summary_naming_the_scope_of_the_parameters_name_is_flagged_when_the_call_binds_another_group()
9a5bf3fc218acb48  other  AllMappings()
4be47768b538de3d  TEST  An_implicit_lambda_parameter_is_never_bound_to_the_group_of_the_same_name()
63fb0ae5840c9b1d  TEST  An_out_variable_is_never_bound_to_the_group_of_the_same_name()
819ffb67e12dec99  other  Anonymity()
f7b9c450938e6e24  other  AnonymousLabel
d3524849302ca231  other  AnonymousMapping()
92284e4ff44769d4  other  ApiGlob
0ed320e013910433  other  ApiSourceFiles()
fd3e451624e9cb3a  other  ArgumentSpan()
70be179fb07528ad  other  AuthorizationKind
27d30b0443802b46  other  BannedForms()
104301b3fed8ba57  other  BodyAfter()
9df2ba02b9fc8cc9  other  BuildScopeCatalogue()
ff02ba94e7b91279  other  CatalogueKeys()
9c40fa7a7ecb81a0  other  ClaimedScope
ab3585c86f776819  other  ClaimedScopes()
ae574f5a7a0f09c9  other  Collect()
5fc7a2539e33a993  other  Compact()
c44917ab3432306e  other  ConstantPath
6531fb0479dd8409  other  ContextApiDirectories()
efa84c88e2f62fd0  other  DeclaredAuthorization()
ce25ca6bda3bb3f8  other  DeclaredPath()
627d3d7d5142e7bd  other  DeclaredPolicy()
f6d5e13ed196c700  other  DeclaredSummary()
030297a69cb695c0  other  DistinctGroups()
fe716dd4c650bf37  other  DistinctPrefixFixtureFile
36924e02263d640c  other  EnclosingBlock()
71a7f3ab6a394bd1  other  EndOfLiteral()
2dccb8316b72e892  other  EndpointFileCount
deb7092c07de6da2  other  EndpointFileReading()
d55f0e91e08ebeb2  other  EndpointFiles()
0f67694d9783f970  other  EndpointGlob
9b31ceac2845bdaa  other  EndpointMapping()
d2ef2791527b6635  other  EndpointSourceFiles()
38daee9fc43a2234  other  EnforcedAuthorization()
ec668acae49a769b  other  EqualsAssignment
5e833a790c8afb95  TEST  Every_anonymous_endpoint_says_what_authenticates_it_instead()
3a47ec50b9109c2f  TEST  Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue()
d159d9af271d0916  TEST  Every_mapping_in_an_endpoint_file_is_read_rather_than_skipped()
38113d2e1e1daeb6  TEST  Every_registered_route_still_enforces_no_scope()
764cd80e84871842  TEST  Every_route_handler_mapping_under_the_api_projects_is_enumerated()
1fea6c7766498b34  TEST  Every_route_mapped_outside_the_readable_shapes_declares_a_catalogue_scope()
d54a6c662d8f070f  TEST  Every_route_mapped_outside_the_readable_shapes_is_registered()
c6e89f155080dd91  TEST  Every_route_mapped_outside_the_readable_shapes_still_exists()
91136c614463f24f  TEST  Every_scope_an_endpoint_enforces_resolves_against_the_catalogue()
e3d450c8eacf05b3  TEST  Every_scoped_endpoint_declares_the_refusal_its_scope_produces()
4424bbcfeaaebc2b  TEST  Every_scoped_endpoint_names_the_scope_it_enforces_in_its_summary()
9a96ae5d2d308b02  other  FirstAuthorizationCall()
7ad041d7963661a1  other  FirstLiteral()
be8db1fda9459ab1  other  FirstLiteralIn()
ba4926945aac950a  other  ForbiddenDeclaration
741b147cb4794cd9  other  ForbiddenDeclarationsIn()
13089eec9ade58ad  other  FormsThisReaderCannotMask()
d3178bc9ed39cc22  other  GroupBinding()
b9f8ec93047ea700  other  GroupDeclaration()
2c0dea9e8f054301  other  GroupHelperDeclaration
effc5d8366c80e3e  other  GroupScope()
85b236032ed340e2  other  GroupSite
2f8ccb56f11c60d2  other  GuardSource
1dd2d975bd29fa21  other  HelperFixture()
f7dab27c5964a637  other  HelperFixtureFile
92fa513e0e435b6b  other  HelperWithOwnScope
4c9e9d6aea62d814  other  IndexOfCall()
aa31fd5a5cec418e  other  IsSource()
fba3c6c7da6c7620  other  JoinRoute()
d1e26fbfbc7d19f0  other  LineAt()
78466244c6ae5f72  other  LocalDeclaration
e9feb982fd375798  other  MappedOutsideTheChain
a74d7ff2c0780d2b  other  MappingSite
140c13d5dcfadd09  other  Masked()
8ea81bf1031e4275  other  Matches()
d37538b188cf07e3  other  Matches()#2
00c48af6a4e0ebe1  other  Mismatch()
262e520ac29bf35d  other  Next()
2d261b62f9495cc7  other  NoSummary
cb27f8424c43a749  TEST  No_route_group_declares_the_refusal_its_members_must_declare()
c3f7f351de7c2452  TEST  No_summary_names_a_scope_other_than_the_one_the_endpoint_enforces()
e1b1b62105ad1cef  other  Omission()
b8618a06665715e9  other  OutsideTheChainMapping()
db019a702f12c52f  other  OutsideTheChainSite
17a5c16c5cb3df09  other  OutsideTheChainSites()
be2dd07bc190b386  other  ParameterListEnd()
2a220a3781bbe9d7  other  ParameterScope()
a9555628cfe0ca7a  other  ParameterScopes()
243e9235fdc682ba  other  PlainHelper
a0deb7f29bcb5e32  other  PrivateConstant()
a8c47ec22673f25d  other  Quoted()
8415946a9b32be04  other  Read()
b2271af98911dcef  other  Read()#2
76c07bc01ab4256e  other  ReadMapping()
8d372383b93556dd  other  ReadRepositoryFile()
d6bfa5bd0a10a85d  other  ReceiverProblem()
64d1c613af3c6234  other  ResolveHelperGroup()
15bd725f414e0fdd  other  RouteBuilderDeclaration
49a072b24465ae44  other  RouteBuilderNames()
d179da244e3b1784  other  RouteGroup()
d13f3ad408a8ed42  other  RouteHandlerMappingCount
d92a99cb14efaa57  other  RouteOutsideTheChain()
d7a651eae0aa0e1b  other  ScopeLabel
855bb2720bdbd4be  other  ScopeLiterals
939141305d3b5492  other  ScopedEndpoint()
3e9f44297a7de440  other  ScopedWithLiteral()
cbdc8a39ca4b8c84  other  SharedSourceScanFiles
533f6239b02fea5c  other  ShouldBeUnreadableAndUnbound()
f5695a205e0c31b8  other  SummaryOf()
db0d6667d77cb6d5  TEST  The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask()
6c234794d5f63436  TEST  The_guard_offers_no_way_to_excuse_an_endpoint()
9ea94760f873895d  TEST  The_refusal_declarations_the_walk_finds_are_all_the_ones_there_are()
6a285e28116e626e  TEST  The_scope_catalogue_is_read_from_the_constants_the_endpoints_cite()
5bc7f88bede476aa  TEST  The_webhook_ingest_summary_describes_the_validation_mode_that_is_actually_the_default()
e3959f91eae06782  TEST  The_whep_hook_summary_names_the_one_scope_its_handler_accepts()
c3721ca08fd9efdd  other  TryJoinLiterals()
2cdf79460bdfe5fc  other  Undeclared()
82c4b95bd5738776  other  UnenforcedByDesign
34449b7c029d88e3  other  UnenforcedRoute()
1194f4e3ebee5485  other  Unescape()
0019a51e7a0796f9  other  Where()
# FILE-MEMBERS-SHA256 0f8120c2d64a1a097d0e960ade271bf5cfc98c5a1e029c0acf1d7ce0ba4571f3
# StatusProducerDeclarationTests.cs members=68 tests=23
d8d0d58533ed8c2d  TEST  A_generic_helper_whose_parameter_is_a_type_parameter_is_never_credited_with_the_group_of_the_same_name()
fafa23bda10510e5  TEST  A_group_local_authorizes_a_mapping_in_its_own_block_when_no_shape_redeclares_the_name()
9a6b8605f2ffa472  TEST  A_group_local_is_not_visible_in_another_method_that_names_a_field_after_it()
224c9540d72e7e08  TEST  A_helper_called_with_an_argument_that_is_not_a_group_declared_in_the_file_binds_no_group()
7b34050d9ef11ef4  TEST  A_helper_called_with_the_group_its_parameter_is_named_after_is_authorized_under_that_group()
d55271735fca5983  TEST  A_helper_called_with_two_different_groups_binds_no_group()
861d0cd05ee00b55  TEST  A_helper_that_is_never_called_in_the_file_binds_no_group_rather_than_its_namesake_local()
5d9c693072d3abc1  TEST  A_helper_whose_parameter_is_typed_by_a_using_alias_is_never_credited_with_the_group_of_the_same_name()
23fdc132d2793187  TEST  A_helper_whose_parameter_name_and_caller_argument_disagree_takes_the_group_the_caller_passed()
549c8d7b289ef421  TEST  A_local_function_parameter_is_never_credited_with_the_group_of_the_same_name()
e83c1da72da61e35  TEST  A_parameter_of_a_method_with_several_parameters_is_never_bound_by_name()
b0b652c99237ab9c  TEST  A_planted_unmaskable_form_is_reported_with_its_file_and_line()
bc6d71742f6dea85  other  AccessKind
028535a4e585abc8  TEST  An_implicit_lambda_parameter_is_never_credited_with_the_group_of_the_same_name()
b2666f0865efca15  TEST  An_out_variable_is_never_credited_with_the_group_of_the_same_name()
167a7744d0096f5e  other  Anonymity
6b9f239061273c28  other  ApiSourceFiles()
0057b84327526d23  other  Authorization
442fc940a9c0bb1e  other  BannedForms()
104301b3fed8ba57  other  BodyAfter()
2a1cedcaa674b0e4  other  BoundGroup()
e89cac2ae616ce76  other  Census
adeea752549ed2b8  other  ChallengeDeclaration
226060b62599989e  other  Classify()
0930b21571b7436a  other  DeclaredStatus
79075da6bcf9d312  other  Describe()
36924e02263d640c  other  EnclosingBlock()
2dccb8316b72e892  other  EndpointFileCount
69527c700f77ce09  other  EndpointFileSuffix
fa115bfa5fba98fa  other  EndpointFiles()
ec668acae49a769b  other  EqualsAssignment
4e0190fb32188379  TEST  Every_authorized_mapping_declares_the_challenge_its_authorization_produces()
d083da3e02bb26c9  TEST  Every_mapping_is_either_authorized_or_explicitly_anonymous()
5f339460d52576be  TEST  Every_mechanism_the_census_calls_derived_names_a_guard_that_exists()
22b05b75be965087  TEST  Every_registered_exception_handler_is_classified_by_the_census()
8d3ec16b7509782b  TEST  Every_route_handler_mapping_under_the_api_projects_is_enumerated()
13089eec9ade58ad  other  FormsThisReaderCannotMask()
941b9daa4139ca0d  other  GroupDeclaration
2c0dea9e8f054301  other  GroupHelperDeclaration
effc5d8366c80e3e  other  GroupScope()
ad1b602c37f5194a  other  GroupScopes()
7dbaf6b5acae8356  other  GuardSource
a5e12252fc351c0e  other  HelperFixture()
c45966c75aa24d25  other  IsRedeclared()
b1733a0fb4294d30  other  MappingCall
4b6099dcdcb704a1  other  Mappings()
a88605ecb0702207  other  Mechanism()
daf2ed959fc0fabd  TEST  No_route_group_declares_the_challenge_its_members_must_declare()
be2dd07bc190b386  other  ParameterListEnd()
2a220a3781bbe9d7  other  ParameterScope()
8c2d6296e12b1376  other  ParameterScopes()
38bac85cd2abfd48  other  Read()
7e25b21a24a24316  other  ReadRepositoryFile()
712ee0588b7dfb4c  other  RegistrationSource
cdb99ad5de3348b4  other  ResolveHelperGroup()
2d9bb11ce7eabb06  other  RouteBuilderParameter
35052d40fe6a47b1  other  RouteGroup()
7a1b1b6c11e17794  other  RouteGroups()
d13f3ad408a8ed42  other  RouteHandlerMappingCount
bc712781923729b0  other  RouteMapping()
e9c21fcfb0d7853a  other  SingleEntryMapping()
2ba3aeb36f9043d7  other  SingleHelperMapping()
c722362270623684  other  Surface()
d9c08c2cd3e9f57d  other  TheSurface
82eabf658eff3517  TEST  The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask()
56df70cc09e4b027  TEST  The_challenge_declarations_the_walk_finds_are_all_the_ones_there_are()
b3fe3df78b4cba23  TEST  The_guard_offers_no_way_to_excuse_an_endpoint()
7ec02a3ef0c0f925  other  Visibility
# FILE-MEMBERS-SHA256 b9aa19a48186810f67a5c5d55220cb52c80a5cad3a3b7036f34b24d1c8a9cf30
```
