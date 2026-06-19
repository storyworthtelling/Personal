// Status page: pick a Google Doc via the Drive Picker, create a project, and watch the background
// worker assemble the graphic novel live over SignalR.

const SCOPES = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/documents.readonly";
const DOC_MIME = "application/vnd.google-apps.document";

let googleConfig = null;
let tokenClient = null;
let pickerLoaded = false;

const chooseButton = document.getElementById("choose");
const hint = document.getElementById("hint");
const projectsDiv = document.getElementById("projects");

init();

async function init() {
  try {
    googleConfig = await fetch("/api/config/google").then(r => r.json());
  } catch {
    hint.textContent = " (could not load Google configuration)";
    return;
  }

  await loadProjects();
  connectProgress();
  waitForGoogle();
}

// The Google scripts load async; poll until both the picker and identity libraries are ready.
function waitForGoogle() {
  const ready = window.google && google.accounts && google.accounts.oauth2 && window.gapi;
  if (!ready || !googleConfig.clientId) {
    if (!googleConfig.clientId) hint.textContent = " (set Google ClientId/ApiKey to enable the picker)";
    return setTimeout(waitForGoogle, 300);
  }

  gapi.load("picker", () => { pickerLoaded = true; });
  tokenClient = google.accounts.oauth2.initTokenClient({
    client_id: googleConfig.clientId,
    scope: SCOPES,
    callback: (response) => {
      if (response && response.access_token) openPicker(response.access_token);
    }
  });

  chooseButton.disabled = false;
  hint.textContent = "";
  chooseButton.onclick = () => tokenClient.requestAccessToken({ prompt: "" });
}

function openPicker(accessToken) {
  if (!pickerLoaded) return;
  const view = new google.picker.DocsView(google.picker.ViewId.DOCUMENTS)
    .setMimeTypes(DOC_MIME)
    .setIncludeFolders(false);

  const picker = new google.picker.PickerBuilder()
    .setOAuthToken(accessToken)
    .setDeveloperKey(googleConfig.apiKey)
    .addView(view)
    .setCallback((data) => onPicked(data, accessToken))
    .build();
  picker.setVisible(true);
}

async function onPicked(data, accessToken) {
  if (data.action !== google.picker.Action.PICKED) return;
  const doc = data.docs[0];
  chooseButton.disabled = true;
  hint.textContent = " creating project…";
  try {
    await fetch("/api/projects", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        fileId: doc.id,
        fileName: doc.name,
        accessToken,
        title: doc.name,
        artStyle: document.getElementById("artStyle").value
      })
    });
    await loadProjects();
  } catch (e) {
    hint.textContent = " (failed to create project)";
  } finally {
    chooseButton.disabled = false;
    hint.textContent = "";
  }
}

async function loadProjects() {
  const projects = await fetch("/api/projects").then(r => r.json());
  renderAll(projects);
}

function connectProgress() {
  const connection = new signalR.HubConnectionBuilder().withUrl("/hubs/progress").withAutomaticReconnect().build();
  connection.on("ProjectUpdated", (status) => upsertProject(status));
  connection.start().catch(() => setTimeout(connectProgress, 2000));
}

const cards = new Map();

function renderAll(projects) {
  projectsDiv.innerHTML = "";
  cards.clear();
  if (!projects.length) {
    projectsDiv.innerHTML = '<p class="empty">No projects yet.</p>';
    return;
  }
  projects.forEach(upsertProject);
}

function upsertProject(status) {
  let card = cards.get(status.id);
  if (!card) {
    const empty = projectsDiv.querySelector(".empty");
    if (empty) empty.remove();
    card = document.createElement("div");
    card.className = "panel";
    cards.set(status.id, card);
    projectsDiv.prepend(card);
  }
  card.innerHTML = renderCard(status);
}

function renderCard(s) {
  const bars = [
    bar("Design bible", s.chunks),
    bar("Reference sheets", s.referenceSheets),
    bar("Panels drawn", s.panels),
    bar("Pages composed", s.pages),
    bar("Volumes (PDF)", s.volumes)
  ].join("");

  const downloads = (s.volumeList || [])
    .map(v => v.pdfReady
      ? `<a href="/api/projects/${s.id}/volumes/${v.number}/pdf">Volume ${v.number} (PDF)</a>`
      : `<span class="empty">Volume ${v.number}…</span>`)
    .join(" ");

  return `
    <div><span class="stage">${escapeHtml(s.title)}</span> — ${escapeHtml(prettyStage(s.stage))}</div>
    <div class="bars">${bars}</div>
    ${s.lastError ? `<p class="err">${escapeHtml(s.lastError)}</p>` : ""}
    ${downloads ? `<p class="vol">${downloads}</p>` : ""}`;
}

function bar(label, p) {
  const total = p ? p.total : 0;
  const done = p ? p.done : 0;
  const pct = total > 0 ? Math.round((done / total) * 100) : 0;
  return `<div>${label}</div><div class="track"><div class="fill" style="width:${pct}%"></div></div><div>${done}/${total}</div>`;
}

function prettyStage(stage) {
  return stage.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function escapeHtml(text) {
  return (text || "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
