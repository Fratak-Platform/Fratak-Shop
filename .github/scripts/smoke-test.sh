#!/usr/bin/env bash
# placeholder – replaced with the full smoke test once the pages are wired.
curl -s -o /dev/null -w "%{http_code} /\n" "$1/"
