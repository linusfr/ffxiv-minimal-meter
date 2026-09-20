# Provenance and licensing

This repository is a derivative work of
[Sansflaire/DamageMeter](https://github.com/Sansflaire/DamageMeter).
Every file under `src/` originates there, renamed and modified.

## The problem

As of 2026-09-20 the upstream repository contains **no LICENSE file**. Under the
Berne Convention and US/EU copyright law, absent an explicit licence grant, the
default is *all rights reserved*: no permission to copy, modify, or redistribute.

GitHub's terms give other users the right to *view and fork* a public repo, but
not to redistribute modified copies outside GitHub, and not to publish derived
binaries.

This repo is currently **public**, and CI publishes built `.zip` artifacts to
GitHub Releases. That combination is redistribution of a modified work without a
licence grant.

## What the LICENSE file does and does not do

This repository carries a plain MIT `LICENSE`, by deliberate choice, with no
clause narrowing it to the parts written here. Note that a licence file does not
enlarge what its author is able to grant: it does not
relicense the derived portions, because a downstream author cannot grant rights
in work they do not own.

So the MIT file is useful — it tells anyone reading exactly which parts they may
take freely, and it is the grant that would be needed if upstream ever relicenses
and this repo becomes redistributable as a whole. It does not, on its own, make
publishing lawful.

## Options

1. **Make this repository private.** Personal use, no redistribution, problem
   gone. Cheapest fix by far, and reversible.
2. **Ask Sansflaire to add a licence.** MIT or Apache-2.0 would permit everything
   here, requiring only that attribution be retained — which the README already
   does. A one-line issue on their repo.
3. **Rewrite from scratch.** Independent implementation against the same Dalamud
   APIs. Expensive, and unnecessary if (2) works.

Until one of those is settled, do not push, do not publish releases, and do not
list this plugin in any Dalamud repository.
