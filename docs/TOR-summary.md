# TOR Summary — SB LSS Repository Platform

> **Source:** `Term of Reference (TOR) SINGLE BUYER LARGE SCALE SOLAR (LSS) REPOSITORY PLATFORM_4000052768 2.pdf` (23 pages, marked **CONFIDENTIAL**)
> **Owner:** Single Buyer Department, Tenaga Nasional Berhad (TNB)
> **Summarised:** 2026-10-02
>
> **Caveat:** this summary was extracted from the PDF. Table and timeline details were partly garbled by extraction and are **reconstructed** — verify figures against the original before relying on them for contractual purposes.

## 1. Introduction

Guidance for the Terms of Reference (TOR) of the **Single Buyer Large Scale Solar (LSS) Repository Platform** ("SB LSS Repository Platform"). Intended as a key reference for vendors and stakeholders developing the platform.

## 2. Background

- In Peninsular Malaysia's electricity market, daily generation planning and variable renewable forecasting are core functions of the Market Operator unit under Single Buyer (SB).
- Least-cost scheduling is required under the *Guidelines for Single Buyer Market (Peninsular Malaysia)*, monitored by the Energy Commission under the Electricity Supply Act 1990.
- **36 LSS plants** currently submit a daily generation forecast plus real-time meteorological data (≥13 parameters, e.g. irradiance, temperature, wind speed; ≥3 and sometimes >10 data points each) via web services.
- With CGPP, LSS5 and future initiatives, a **centralized repository** is needed to consolidate storage, enable access/sharing, and scale to future projects.

## 3. Project overview

- LSS plants submit **DDQ** (Declared Daily Quantity — generation forecast for **day ahead, week ahead, four months ahead**, and current day) and **MMF** (Meteorological Measuring Facilities — real-time weather data) through web services in standard formats/naming conventions.
- The platform automates collection and management: obtain web-service data → store in a database → compile into customised file formats → email to SB and expose download on a website (raw and compiled data).
- Reference architecture, data-flow and user-access diagrams are in Figures 1–4 of the TOR (SB DMZ web server, SB Infra DB server, DR site, SB AD for user lookup).

**Technology stack (§3.4):**

| Layer | Choice |
| --- | --- |
| Web / application | Blazor (C#), .NET Core 8 |
| Operating system | Windows Server 2022 |
| Database | MSSQL Server 2022 |

## 4. Objectives

1. Centralized repository for collection, storage and management of data from all current and future LSS plants in Peninsular Malaysia.
2. Ensure accuracy, consistency and reliability, with automated compilation into customised file formats for daily planning and demand forecasting.
3. Enhance transparency and accessibility via a secure, user-friendly platform for authorised users.
4. Support operational efficiency and least-cost scheduling with timely, accurate generation and meteorological data.
5. Align with Malaysia's energy transition goals (renewable integration monitoring and policy support).

## 5. Scope of work

Four tasks, all complying with TNB ICT policies.

### Task 1 — Establish the SB LSS Repository Platform

**Platform**
1. Design/implement architecture per Paragraph 3 and TNB ICT policies.
2. Develop **API integrations** to obtain forecast and MMF data automatically at predetermined timings and on manual trigger.
3. Automatically store API responses in a centralized DB (predetermined timings + manual trigger) with **timestamping, anomaly detection and audit trails**:
   - Detect anomalies (zeros, out of range, data unavailable, failed API requests); report periodically by email; persist anomaly details.
   - Detect data interval from the API response (15/30/60 min, etc.).
   - Sample data to other intervals when generating reports (without changing original data).
4. Secure web interface for authorised users to query/download raw and compiled data:
   - Access control + authentication via **SB Active Directory (AD)**.
   - Query by date range, interval, data type (Five Day Ahead, Week Ahead, Four Months Ahead, Rolling 24 Hours, MMF, MMF parameters), update type (auto/manual) and timestamp.
   - Missing data filled from the latest available day, with the platform stating it is missing and which day was used — without altering source data.
5. Compile data into customised file formats, email automatically to a user list, and provide manual extraction on the website (same missing-data substitution rule).
6. Enable resampling, computation and dynamic API expansion for future plants.
7. Automatically reflect new API points across request, database, website, customised files, etc.
8. Log file on data anomalies and unavailability.
9. Store backend activities (logins, user activities, audit trails, etc.).

**Cybersecurity**
10. **RBAC** integrated with SB AD; secure authentication via **LDAPS**; audit logging.
11. Encrypt **in transit (TLS 1.2+)** and **at rest (AES-256)**.
12. Place API and web servers in a **DMZ**.
13. Secure coding practices, code scanning, API security (rate limiting, logging, patching).
14. Integrate servers with TNB security controls: **SIEM (Splunk), AD, SUPM, CMDB, antivirus**, etc.

**Hosting**
15. Configure Development, Production and DR server needs.
16. Migrate application, database and configuration from Dev/Prod/DR on SB infra to a future Single Buyer physical infra host.

### Task 2 — Documentation

- **Governance:** OTC handover, CAB, CSRA, TNB Security Assessment & Vulnerability Remediation, VA, Penetration Testing, Security Assessment, Secure Code Scanning.
- **System design/dev:** User Business Requirements; FSD/FTD; CATP; source code, architecture, database, API, website and report documentation.
- **Testing:** Unit, SIT, PAT, UAT (scripts + final reports).
- **Operational/support:** Backup & DR plan; System Configuration & Operation Manual; Change Management Plan; SOP; Administrator & Maintenance Guides; Troubleshooting & Security Guidelines.
- **User:** User Training Manual; End-User Manual.

### Task 3 — Backup activity and Disaster Recovery (DR)

- Documented, regular backup plan; participate in/support backup testing; monitor backup processes; compatibility with SB's **AVAMAR** backup.
- DR plan document; execute DR and periodic DR testing; manage/maintain the DR site; DR test support and maintenance.

### Task 4 — Training

- SB staff: "API and Web Services Fundamentals" (SOAP, REST, integration).
- SB and TNB ICT users (System Administrators, Data Owners, Data Custodians): usage, administration, functionality.
- Knowledge transfer: source code/architecture/database; API request/response and parameters; platform operations, downloads, account management; future plant integration.
- Routine maintenance and basic troubleshooting; security best practices; deliver all training materials.

## 6. Warranty

Minimum **12 months** from final acceptance, during which defects/errors/issues within scope must be rectified.

## 7. Support and maintenance

**12 months** after warranty. Scope: system support/user assistance; software/platform maintenance; data management & validation; security/backup/compliance; availability & performance; enhancements/continuous improvement; licensed & customised software maintenance.

**Service levels (Table 2):**

| Severity | Description | Ack. | 1st response | Update freq. | Resolution |
| --- | --- | --- | --- | --- | --- |
| Level 1 | Complete software failure | 15 min | 1 hour | Every 1 hour | Within 1 business day |
| Level 2 | Major bugs (part/module fully inoperable) | 15 min | 4 hours | Every 4 hours | Within 1 business day |
| Level 3 | Minor bugs (no full outage) | 30 min | 8 hours | Every 8 hours | Within 3 business days |
| Level 4 | Minor bugs with workaround | 2 hours | 12 hours | Every 12 hours | Within 5 business days |

**KPI (Table 3):** helpdesk **24×7/365**; 99.9% of requests logged/attended within defined time frames; incident reporting — Preliminary + Final Incident Report & RCA: Sev1 prelim 4 h / final 3 days; Sev2 prelim 8 h / final 5 days; Sev3 & Sev4 prelim 24 h / final 5 working days.

## 8. Business continuity (backup and DR)

Aligned to **ISO 22301, ISO 27001, NIST SP 800-34**.

- **Backup (§8.1):** policy-based backup of source code, configs, databases, logs, data; leverage existing SB backup infrastructure (ECS Operations); automated backups; **daily incremental + weekly full**; retention **30 days operational / 90 days audit & compliance**; offsite/redundant storage; **quarterly restore verification tests**.
- **DR environment (§8.2):** mirrors production; **RPO ≤ 1 hour**, **RTO ≤ 2 hours**; controlled failover/failback; documented activation/escalation/communication; **annual DR drill**.
- **HA & resilience (§8.3):** redundancy at application/database/network; load balancing and clustering; DB replication/failover for API ingestion and report generation.
- **Monitoring & alerting (§8.4):** integrate with monitoring tools; alerts on backup failure, replication lag, capacity thresholds.
- **Maintenance & documentation (§8.5):** up-to-date docs; approved architecture changes; submit test/drill/recovery records.
- **Roles (§8.6):** Vendor implements/maintains/validates; SB/ICT reviews, endorses, participates, confirms post-recovery integrity.
- **Deliverables (§8.7):** BC & DR Plan; Backup & Restoration Policy/Procedures; DR Test & Verification Reports; Contact & Escalation Matrix.

## 9. Project deliverables

Main deliverables (Table 4), to be completed within **24 weeks or less** from kick-off:

**Task 1 — Establish platform:** architecture per §3/§5; **API integration module (>36 APIs)** with admin endpoint registration/management; centralized DB (timestamping, anomaly detection, audit trails); web platform with **RBAC + SB AD**; **automated reporting engine with CSV generation and substitution logs**; computation/resampling; security controls (RBAC, LDAPS, TLS 1.2+, AES-256, DMZ, audit logging); secure code scanning reports; integration with SIEM/Splunk, AD, SUPM, CMDB, antivirus; Dev/Prod/DR hosting with migration capability; secure handling of sensitive files; SSDLC compliance.

**Task 2 — Documentation:** governance submissions; BRD; FSD/FTD; CATP; source/DB/architecture/API/website/report docs; Unit/SIT/PAT/UAT scripts & reports; Backup & DR plan; ops manuals/SOP/admin/maintenance/troubleshooting/security; User Training Manual and End-User Manual.

**Task 3 — Backup & DR:** backup plan & schedule; backup test reports; DR plan & schedule; DR test reports; §8 documentation.

**Task 4 — Training:** API/web services fundamentals; platform usage/functionality; knowledge transfer (source/architecture/DB/operations); User Training Manual; System Administration Guide; Troubleshooting Guide; Security Guidelines; End-User Manual.

**Other deliverables (Table 5):** 1 kick-off meeting; ≥8 physical progress meetings (minutes within 5 working days); **minimum 6 days** user training/knowledge transfer.

## 10. Proposed project timeline

- **24 weeks** project development.
- Minimum **12 months** warranty.
- **12 months** support and maintenance.
- Total expected duration: **up to 2 years and 24 weeks**. Detailed schedule in Attachment 1 (kick-off, design/application development, development & testing, go-live/DR, documentation & handover).

## 11. Project fees

Vendor provides contract price per task (RM) and support/maintenance price, plus a manning schedule (man-hours/days per team-member level). Tables 6–7 (prices left blank in TOR).

## 12. Payment schedule

**Project (Table 8):**

| Schedule | Deliverables | % |
| --- | --- | --- |
| 1st — Project kick-off | Approved BRD; project plan & timeline | 10% |
| 2nd — Design | Approved FSD/FTD | 10% |
| 3rd — Development & testing | Dev config, Unit Test, SIT, approved SIT/Unit docs, UAT + approved UAT docs | 30% |
| 4th — Go-live | Prod config, backup & DR established/approved, CSRA/VA/pen-test/secure-code reports, post-launch support, CAB docs, deployment, CATP & PAT approval, successful go-live | 30% |
| 5th — Documentation & handover | Training/knowledge transfer, approved User Training Manual and full docs, source code handover, handover to SB/ICT | 15% |
| 6th — Post-warranty | 12-month warranty completion, defects closed, cybersecurity compliance confirmed | 5% |

**Support & maintenance (Table 9):** Commencement 25%; Quarter 1 25%; Quarter 2 25%; Quarter 3 & closure 20%; retention 5%.

## 13. Intellectual property ownership

All IP (source code, design, documentation, work products) is **solely Single Buyer / TNB**. SB receives **complete, unencrypted source code** with meaningful comments, and full rights to modify/enhance.

## 14. Data ownership and confidentiality

Confidentiality/non-disclosure obligations **survive termination and bind in perpetuity**. Vendor bound by TNB data confidentiality terms.

## 15. Terms and conditions

Vendor agrees to TNB's separate terms and conditions, read together with this TOR. Single Buyer may amend the TOR in whole or in part.

---

## Appendix — Relevance to this prototype (`Lss.EntraLoginTest`)

This repository is a small prototype that exercises some platform concerns:

| TOR requirement | Current prototype | Status |
| --- | --- | --- |
| Blazor + MSSQL web platform | `Lss.EntraLoginTest` (Blazor, SQL `LSSRepo`) | Prototype exists |
| Framework version | TOR: **.NET Core 8**; prototype: **net10.0** | ⚠️ Mismatch to reconcile |
| RBAC + directory auth | TOR: **SB Active Directory / LDAPS**; prototype: **Microsoft Entra ID** | ⚠️ Different identity model |
| Reporting / email delivery | Daily report email module (Microsoft Graph `Mail.Send`) | Prototype exists |
| API collection (DDQ + MMF) | Sample responses in `C:\Users\User\Documents\LSS\Sample Data` | Not yet integrated |
| Anomaly detection, resampling, substitution logs | — | Not yet built |

**Open items to confirm:** target framework (.NET 8 vs current), identity provider (on-prem AD/LDAPS vs Entra ID), and where the sample API data maps to ingestion/reporting modules.
