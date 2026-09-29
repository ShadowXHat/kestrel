# Kestrel Frontend UI Redesign Prompt for AntiGravity

## Context
You are redesigning the **Kestrel** frontend — a Windows Event Log analyzer for SOC analysts.

**Project:** Kestrel (https://github.com/your-org/kestrel)
**Location:** `D:\Work\Projects\kestrel\frontend\`
**Tech stack:** React 18 + TypeScript + Vite + AG Grid Community + ECharts + Tailwind CSS
**Theme:** Dark SOC theme (dark backgrounds, monospace data, professional look)
**Existing UI:** Functional but plain/temporary — needs visual upgrade

## What to Do

1. **Load the design skills** that are available in `.agents/skills/`:
   - `design-taste-frontend` — polished professional UI patterns
   - `frontend-design` — React component design best practices
   - `minimalist-ui` — clean, minimal dark-themed design system
   - `design-an-interface` — systematic interface redesign process

2. **Review the current UI** in `frontend/src/`:
   - `pages/Login.tsx`, `Dashboard.tsx`, `Events.tsx`, `Search.tsx`, `Alerts.tsx`, `Settings.tsx`
   - `components/Sidebar.tsx`, `Layout.tsx`
   - `App.tsx` (routing)
   - `index.css` (styles)

3. **Redesign requirements:**
   - Keep ALL functionality intact (AG Grid, ECharts, SignalR, routing)
   - Keep TypeScript types and API calls unchanged
   - Keep Tailwind CSS — upgrade the styling only
   - Apply a **dark professional SOC theme** (dark backgrounds, accent colors for alerts, monospace for data)
   - Use **glassmorphism** effects where appropriate (backdrop-blur, semi-transparent panels)
   - Make the AG Grid table visually polished (dark header, row hover effects, status badges)
   - Add **smooth animations** and transitions
   - Ensure responsive design
   - Use modern typography and spacing

4. **Design principles from skills:**
   - Clean, minimal, functional — no clutter
   - Dark mode first (SOC analysts work in dark environments)
   - Data-dense but readable
   - Color-coded severity levels (info=blue, warning=yellow, error=red, critical=purple)
   - Professional look suitable for enterprise SOC use

5. **Do NOT:**
   - Change any API or backend code
   - Remove any functionality
   - Change the component structure (pages, components stay the same)
   - Modify the AG Grid or ECharts configuration logic
   - Change the routing structure
   - Break TypeScript types

6. **Do:**
   - Upgrade all CSS/Tailwind classes
   - Add glassmorphism cards and panels
   - Add hover effects, transitions, animations
   - Add status badges and visual indicators
   - Improve the sidebar with icons and better spacing
   - Add a professional navbar/header if needed
   - Make the login page polished
   - Add loading skeletons and skeleton screens
   - Add toast notifications style for alerts

## Output
- Update all files in `frontend/src/` with the improved UI
- Keep all `.tsx`, `.ts`, and `.css` files in place
- Do not create new files unless absolutely necessary
- Keep the `package.json` unchanged

## Starting Point
The project has a functional but temporary UI. Focus entirely on visual improvement while preserving all logic and functionality.
