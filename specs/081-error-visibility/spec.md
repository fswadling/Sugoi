# Feature Specification: Error Visibility

**Feature Branch**: `081-error-visibility`

**Created**: 2026-10-12

**Status**: Draft

**Input**: User description: "A console log is pretty hard to spot - we want a visible error if we start getting lots of errors logged"

## Origin

`specs/080-user-path-hardening` made the application degrade gracefully instead of crashing: a cue operation
whose target is gone, a refused creation, and a bad asset now perform nothing and **report a one-line message
to the console**. That is the right behaviour, but the report is invisible in a running application — an
author only sees it if they happen to be watching a terminal. Worse, some of these reports **repeat**: a cue
re-fired by a trigger, or a held key, can emit the same line every frame. So a real problem (an operation
that keeps missing its target, an asset failing on every load) is easy to miss and impossible to notice at a
glance.

This feature makes those reports **visible in the application**: when reports start accumulating, the
application shows it — a counted, de-duplicated record the author can see and inspect without a terminal.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Seeing that the application is producing errors (Priority: P1)

An author is playing or editing. The application starts reporting non-fatal problems — an operation that
keeps missing its target, or an asset that keeps failing to load. Without leaving the application or watching
a terminal, the author notices that problems are being reported, and can look at what they are.

**Why this priority**: it is the whole point of the feature and the smallest complete slice. The reports
`080` added are currently invisible, so nothing pays off until a count is on screen. It is independently
testable and delivers value on its own.

**Independent Test**: cause a report to be made; assert the error count becomes visible in the application's
own interface with no terminal involved. Cause the same report to be made many times; assert the count rises
while the detail shows a single, de-duplicated entry carrying how many times it happened.

**Acceptance Scenarios**:

1. **Given** no problems have been reported, **When** the interface is shown, **Then** no error indicator
   appears.
2. **Given** a report has been made, **When** the interface is shown, **Then** the number of reports is
   visible and the most recent report's text is readable.
3. **Given** the same report is made many times, **When** the detail is inspected, **Then** it appears as a
   single entry carrying how many times it occurred — not as many separate lines.
4. **Given** reports have accumulated past a threshold, **When** the interface is shown, **Then** the
   indicator is made prominent — it draws attention to itself — rather than reading as an ordinary number.

### User Story 2 - Inspecting what was reported (Priority: P2)

An author sees that errors have accumulated and wants to know *what* they were. They can open (or bring
forward) the detail that lists the distinct reports and how often each has occurred, so they can act on the
cause.

**Why this priority**: the count tells the author *that* something is wrong; the detail tells them *what*.
It is a natural second slice and independently testable once the count (US1) exists.

**Independent Test**: with several distinct reports made, open the detail and assert each distinct report is
listed with its repeat count, most recent first.

**Acceptance Scenarios**:

1. **Given** several distinct reports have been made, **When** the detail is opened, **Then** each distinct
   report is listed with how many times it occurred.
2. **Given** the visible indicator is being shown, **When** the author acts on it, **Then** the detail is
   brought to the front so it can be read.

### Edge Cases

- A report fires every frame (a held key, a re-fired trigger): the count rises without the detail being
  flooded with duplicates.
- The surface the indicator lives on is collapsed or closed by the author: the indicator is still seen.
- An extremely large number of reports: the count saturates rather than overflowing or misbehaving.
- Reports made before any interface exists (during startup): the count is correct once the interface appears.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every non-fatal report the application makes MUST be recorded in one place, with a running
  total of how many have been made.
- **FR-002**: Reports MUST be de-duplicated by their text: a repeated report increments a recorded count on
  one entry rather than adding another.
- **FR-003**: The application MUST show the current report total in its own interface whenever it is
  non-zero, and MUST NOT show an error indicator when it is zero.
- **FR-004**: The interface MUST make the distinct reports inspectable, each with how many times it occurred.
- **FR-005**: Once the total passes a threshold, the application MUST make the indicator prominent — it must
  draw attention to itself rather than reading as an ordinary number.
- **FR-006**: The console output MUST be unchanged: a report is still written to the console exactly as
  before — the visible record is additive.
- **FR-007**: Recording MUST add no per-frame cost when no reports have been made.
- **FR-008**: The count and the detail MUST be reachable from the application's existing interface; no new
  kind of window, dialog, or notification system is introduced.

### Key Entities

- **Report**: one non-fatal message the application makes; carries its text and how many times it has
  occurred.
- **Report total**: how many reports have been made during the current run.
- **Indicator**: the part of the interface that presents the total, and from which the detail is reached.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the failures `080` handles occur, an author can tell that the application is reporting
  problems **without a terminal** — the total is visible in the application's interface.
- **SC-002**: A report made `n` times reads as **one** entry with count `n`, never as `n` entries.
- **SC-003**: A frame in which no report is made adds no per-frame cost to the application.
- **SC-004**: The console output for a given report is unchanged from before this feature.
- **SC-005**: A run in which nothing is reported shows no error indicator at all.

## Assumptions

- The audience is the project's author (in the editor and in play); the "interface" is the application's
  existing on-screen surfaces.
- The console remains the durable record; the visible indicator is a convenience read-out over it.
- The threshold at which the indicator becomes prominent is a small fixed count, decided during
  implementation, not a user setting.
- De-duplication is by exact message text; two different messages are two distinct entries.
- This feature does not introduce a modal dialog or a transient notification ("toast") system — existing
  surfaces are sufficient.
