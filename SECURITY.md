# Security

Treat models and native backend libraries as executable-trust inputs. Load only
trusted, hash-verified artifacts and retain their provenance. The runtime does not
download models or send inference data over a network. Build/download scripts use
the network only when explicitly invoked or when optional CMake fetching is enabled.

The model can follow malicious instructions embedded in its input despite prompt
framing. Typed output prevents invalid categories, not incorrect decisions. Enforce
authorization, input limits and external side-effect policy in the host application.

Report a suspected vulnerability privately using the repository's GitHub Security
Advisory feature once hosted and enabled. Do not include secrets or sensitive user
data in public issues. There is no separate reporting mailbox configured yet.
Only the current development version is maintained; no security SLA is offered.
