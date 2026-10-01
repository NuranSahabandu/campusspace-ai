# Architecture Decision Records

One file per decision, named `NNNN-short-title.md`.

## Notes

- React 19 instead of plan's 18: Vite default; all chosen libraries support it.
- Charts: `@mui/x-charts` 9.14 (MIT) instead of the plan's Recharts (§6). Measured with one lazy bar chart in our
  build: x-charts chunk 276.4 kB / 86.4 kB gzip vs recharts 3.10 346.2 kB / 100.3 kB gzip; the main chunk is unchanged
  either way (lazy only). x-charts shares @mui/material and @mui/x-internals with the DataGrid we already ship and
  follows the MUI theme. Only `web/src/features/reports/charts/` imports it.
