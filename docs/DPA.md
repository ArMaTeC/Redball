# Data Processing Agreement (DPA)

**Effective Date:** March 2025  
**Version:** 1.0

This Data Processing Agreement ("DPA") forms part of the Terms of Service between you ("User", "Data Subject") and the Redball project maintainers ("Data Controller") regarding the processing of personal data through the use of the Redball application.

---

## 1. Definitions

For the purposes of this DPA:

- **"Personal Data"** means any information relating to an identified or identifiable natural person.
- **"Processing"** means any operation performed on Personal Data (collection, recording, storage, etc.).
- **"Data Controller"** means the entity which determines the purposes and means of processing Personal Data.
- **"Data Processor"** means the entity which processes Personal Data on behalf of the Controller.
- **"Data Subject"** means the natural person to whom the Personal Data relates.

---

## 2. Roles and Responsibilities

### 2.1 Data Controller

The Redball maintainers act as Data Controller for:

- GitHub account information when you contribute code or report issues
- Data you voluntarily submit through feedback forms (if transmitted)

### 2.2 User as Data Controller

You act as Data Controller for:

- All configuration data stored in your local `Redball.json`
- All analytics data stored locally in `analytics.json`
- All log files generated on your machine

---

## 3. Data Processing Activities

### 3.1 Local Processing (No External Transfer)

By default, Redball operates entirely locally:

| Activity | Data Processed | Storage Location | Legal Basis |
| --- | --- | --- | --- |
| Configuration storage | User preferences | Local machine | Legitimate interest (functionality) |
| Log generation | Operational events | Local machine | Legitimate interest (debugging) |
| Analytics (opt-in) | Feature usage counts | Local machine | Consent |
| Session state | Application state | Local machine | Legitimate interest (user experience) |

### 3.2 Optional Cloud Processing

Only if you explicitly enable cloud analytics:

| Activity | Data Processed | Recipient | Legal Basis |
| --- | --- | --- | --- |
| Usage analytics | Anonymized feature counts | Configured analytics endpoint | Consent |

---

## 4. Rights of Data Subjects

Under GDPR and similar regulations, you have the following rights:

### 4.1 Right to Access

You can access all your data at any time:

- Configuration: `Redball.json`
- Logs: `Redball.log`
- Analytics: `analytics.json`

### 4.2 Right to Rectification

You can modify any data by editing the JSON files or through the Settings UI.

### 4.3 Right to Erasure (Right to be Forgotten)

To delete all your data:

```powershell
# Remove all Redball data
Remove-Item "$env:LocalAppData\Redball" -Recurse -Force
```

### 4.4 Right to Data Portability

All data is stored in standard JSON format and can be easily exported.

### 4.5 Right to Object

You can disable any data processing at any time through Settings.

---

## 5. Data Security Measures

Redball implements the following technical and organizational measures:

### 5.1 Technical Measures

- **Local-only storage** by default
- **No cloud transmission** without explicit opt-in
- **Standard file system permissions** (Windows ACLs apply)
- **No network listeners** (except optional update checks)

### 5.2 Organizational Measures

- **Open source code** for public audit
- **Minimal data collection** principle
- **Clear documentation** of all data processing

---

## 6. Data Breach Notification

Given that all data is stored locally on your machine, the project maintainers cannot access or lose your data. However, if a security vulnerability is discovered:

1. We will publish a security advisory on GitHub
2. We will release a patch as soon as possible
3. We recommend you keep the application updated

---

## 7. Subprocessors

Redball does not use any subprocessors for data processing. All third-party dependencies are:

- Open source libraries (NuGet packages)
- Local-only execution (no external services called)

Full list of dependencies is available in the SBOM (Software Bill of Materials).

---

## 8. International Data Transfers

By default, no data leaves your machine. If you optionally enable:

- **Update checking**: Data goes to GitHub's API (US-based)
- **Cloud analytics** (if configured): Data goes to your specified endpoint

Both are subject to your explicit opt-in.

---

## 9. Data Retention

### 9.1 Automatic Retention Periods

| Data Type | Retention Period | Deletion Method |
| --- | --- | --- |
| Configuration | Until user deletes | Manual deletion |
| Logs | Rotates at size limit | Automatic rotation |
| Analytics | Until user clears | Manual or via UI |
| Session state | Until next start | Automatic cleanup |

### 9.2 No Data Retention by Project

The Redball maintainers do not retain any user data on their systems.

---

## 10. Changes to this DPA

We may update this DPA to reflect:

- Changes in data processing activities
- Changes in applicable law
- Changes in project structure

Updates will be published in:

- The project's GitHub repository
- Release notes for new versions

---

## 11. Contact Information

For questions about this DPA or your data rights:

- **GitHub Issues:** <https://github.com/ArMaTeC/Redball/issues>
- **GitHub Security:** <https://github.com/ArMaTeC/Redball/security>

For formal DPA inquiries from organizations:

- Include "DPA Request" in the issue title

---

## 12. Acceptance

By using Redball, you acknowledge that you have read and understood this DPA and consent to the processing of your personal data as described herein, subject to your configuration choices.

---

**This DPA is effective as of March 2025.**
