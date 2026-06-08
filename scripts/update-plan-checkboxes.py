import re
from pathlib import Path

updates = {
    "F-003-test-projesi.md": [("Test projeleri", " — tests/WindowsAiAssistant.Tests")],
    "F-002-actiongate-politika.md": [("Allowlist", " — `ActionPolicy.SafeShortcuts`")],
    "F-010-uia-hata-iletimi.md": [("UIA fail", " — `PromptBuilder` + `ObservationUiCapture`")],
    "F-REL-01-hata-yayilimi.md": [("Fail action", " — `ActionResult.ErrorCode` → `ActionResultJson`")],
    "F-004-vision-pii.md": [
        ("Vision kapalı", " — `VisionAttachmentPolicy`"),
        ("Kullanıcı ayarlardan", " — UnifiedSettings vision toggle"),
    ],
    "F-006-mouse-koordinat-dpi.md": [
        ("Primary + secondary", " — kod tamam; **manuel:** 2+ monitör"),
        ("SendInput fail", " — `DebugAgentLog` F006"),
    ],
    "F-008-singleton-di.md": [
        ("Her agent run", " — `ResetForSession`, `ResetTransientRunState`"),
        ("Paralel run engeli", " — `AgentRunCoordinator.TryEnterRun`"),
    ],
    "F-009-shell-guvenlik.md": [
        ("Bilinen destructive", " — `ShellSecurityPolicy` + `ActionGateTests`"),
        ("Onaylı shell", " — audit log F009"),
    ],
    "F-ARCH-01-session-scope-di.md": [
        ("Paralel run", " — `AgentRunScope` + `BeginSession(runId)`"),
        ("Singleton handler", " — handler stateless"),
    ],
    "F-COM-01-instance-reuse.md": [
        ("Açık instance", " — kod tamam; **manuel:** açık Office ile doğrula"),
        ("Instance yokken", " — `Activator.CreateInstance` fallback"),
    ],
    "F-COM-02-com-allowlist.md": [
        ("Allowlist dışı", " — `ComAllowlistValidatorTests`"),
        ("Allowlist içi", " — Sensitive onay korunuyor"),
    ],
    "F-016-legacy-settings-page.md": [
        ("Solution", " — grep temiz"),
        ("Tüm ayar sekmeleri", " — `UnifiedSettingsPage`"),
    ],
    "F-PROMPT-01-etkilesim-hiyerarsisi.md": [
        ("Prompt metninde", " — `PromptBuilder` TOOL PRIORITY"),
        ("elementId format", " — prompt örnekleri"),
    ],
    "F-022-debug-agent-log.md": [
        ("Release publish", " — `EnableDebugAgentLog=false` varsayılan"),
        ("Debug modda", " — DEBUG build varsayılan açık"),
    ],
    "F-020-whisper-cold-start.md": [
        ("İlk kullanıcı STT", " — kod tamam; **manuel:** latency ölçümü"),
        ("Warm-up hata", " — lazy load fallback"),
    ],
    "F-017-observation-maliyet.md": [
        ("Statik ekranda", " — incremental reuse + `CaptureDurationMs`; **manuel:** benchmark"),
        ("UI değişince", " — fingerprint/action sonucu değişince full capture"),
    ],
    "F-025-agents-md.md": [
        ("AGENTS.md mevcut", " — kök `AGENTS.md`"),
        ("ActionGate ve test", " — belgelenmiş"),
    ],
    "F-026-httpclient-lifecycle.md": [
        ("TTS yolu", " — static `SharedHttp`"),
        ("Ardışık TTS", " — kod tamam; **manuel:** stabilite"),
    ],
    "F-024-readme-mimari.md": [
        ("Yeni geliştirici", " — README geliştirici bölümü"),
        ("Üç ana proje", " — mimari tablo"),
    ],
}

base = Path(__file__).resolve().parents[1] / "docs" / "plan" / "L3"
for fname, rules in updates.items():
    path = base / fname
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    out = []
    for line in lines:
        if line.strip().startswith("- [ ]"):
            for key, suffix in rules:
                if key in line:
                    line = re.sub(r"^- \[ \]", "- [x]", line.rstrip()) + suffix + "\n"
                    break
        out.append(line)
    path.write_text("".join(out), encoding="utf-8")

print("updated", len(updates), "files")
