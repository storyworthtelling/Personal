// Package gmbridge is a deliberately tiny, gomobile-friendly shim over libgm
// (the reverse-engineered Google Messages-for-Web client maintained for
// mautrix-gmessages). Post embeds the resulting .aar via a .NET binding
// project; this surface is kept to primitives and one callback interface so
// the C# side never touches Go semantics.
//
// Post is a Messages-for-Web client of Google Messages on the same phone:
// Messages keeps the texting role (and with it RCS), and Post reads and sends
// through the same channel messages.google.com uses.
//
// Build (requires Android NDK + gomobile; see build.sh):
//
//	./build.sh
//
// Written against libgm as vendored by go.mau.fi/mautrix-gmessages v0.6.4, and
// against that module's own connector (pkg/connector) for the order of calls
// libgm expects: FetchConfig before StartGaiaPairing, Disconnect after
// FinishGaiaPairing, a fresh Connect on the saved session. libgm tracks
// Google's protocol changes; pin bumps in go.mod may need small adjustments
// here — that is the point of this shim: churn stays here.
package gmbridge

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"sync"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"go.mau.fi/mautrix-gmessages/pkg/libgm"
	"go.mau.fi/mautrix-gmessages/pkg/libgm/events"
	"go.mau.fi/mautrix-gmessages/pkg/libgm/gmproto"
)

// EventHandler receives every bridge event as one JSON document. The shapes are
// decoded by Codex.Domain.Post.BridgeEvents, which is the contract's other half:
//
//	{"type":"message", ...message fields...}
//	{"type":"conversation", ...conversation fields...}
//	{"type":"paired","session":"..."}
//	{"type":"pairFailed","reason":"wrongEmoji|cancelled|timeout|noDevices|phoneNotResponding|signedOut|other","error":"..."}
//	{"type":"sessionChanged","session":"..."}
//	{"type":"loggedOut","reason":"..."}
//	{"type":"connection","state":"ready|listenError|recovered|phoneNotResponding|phoneResponding|inactive|active|fatal","error":"..."}
//	{"type":"log","message":"..."}
type EventHandler interface {
	OnEvent(eventJson string)
}

// Bridge wraps one libgm client session.
type Bridge struct {
	client  *libgm.Client
	handler EventHandler

	lock          sync.Mutex
	pairingCancel context.CancelFunc
	// What a send needs that a conversation id alone does not carry: the
	// participant id the phone sends as in that conversation, and the SIM behind it.
	outgoingIDs map[string]string
	sims        map[string]*gmproto.SIMCard
	selfIDs     map[string]bool
}

// NewBridge creates a bridge. sessionJson is the blob previously returned by a
// "paired" or "sessionChanged" event ("" for a fresh, unpaired session).
func NewBridge(sessionJson string, handler EventHandler) (*Bridge, error) {
	authData := libgm.NewAuthData()
	if sessionJson != "" {
		if err := json.Unmarshal([]byte(sessionJson), authData); err != nil {
			return nil, fmt.Errorf("restoring gmbridge session: %w", err)
		}
	}
	b := &Bridge{
		handler:     handler,
		outgoingIDs: map[string]string{},
		sims:        map[string]*gmproto.SIMCard{},
		selfIDs:     map[string]bool{},
	}
	b.client = libgm.NewClient(authData, nil, zerolog.Nop())
	b.client.SetEventHandler(b.handleEvent)
	return b, nil
}

// StartGoogleAccountPairing pairs with the Google account whose messages.google.com
// sign-in cookies are given (a JSON object of name → value, read by Post from its
// own sign-in page). Returns the emoji the owner must pick in Google Messages;
// the outcome arrives later as a "paired" or "pairFailed" event.
func (b *Bridge) StartGoogleAccountPairing(oauthCookiesJson string) (string, error) {
	var cookies map[string]string
	if err := json.Unmarshal([]byte(oauthCookiesJson), &cookies); err != nil {
		return "", fmt.Errorf("parsing pairing cookies: %w", err)
	}
	b.client.AuthData.SetCookies(cookies)

	ctx, cancel := context.WithCancel(context.Background())
	if err := b.client.FetchConfig(ctx); err != nil {
		cancel()
		return "", fmt.Errorf("fetching the Messages config before pairing: %w", err)
	}
	emoji, session, err := b.client.StartGaiaPairing(ctx)
	if err != nil {
		cancel()
		b.client.Disconnect()
		return "", fmt.Errorf("%s: starting Google-account pairing: %w", pairingReason(err), err)
	}

	b.lock.Lock()
	b.pairingCancel = cancel
	b.lock.Unlock()

	// FinishGaiaPairing blocks until the owner picks the emoji in Google Messages.
	go func() {
		defer cancel()
		_, err := b.client.FinishGaiaPairing(ctx, session)
		// The pairing client is done either way; Post connects again from the saved session.
		b.client.Disconnect()
		if err != nil {
			b.emit(map[string]any{"type": "pairFailed", "reason": pairingReason(err), "error": err.Error()})
			return
		}
		blob, err := json.Marshal(b.client.AuthData)
		if err != nil {
			b.emit(map[string]any{"type": "pairFailed", "reason": "other", "error": "exporting the session: " + err.Error()})
			return
		}
		b.emit(map[string]any{"type": "paired", "session": string(blob)})
	}()
	return emoji, nil
}

// CancelPairing abandons a pairing in progress (libgm tells the phone it was cancelled).
func (b *Bridge) CancelPairing() {
	b.lock.Lock()
	cancel := b.pairingCancel
	b.pairingCancel = nil
	b.lock.Unlock()
	if cancel != nil {
		cancel()
	}
}

// pairingReason maps libgm's pairing errors to the stable codes Post words for the owner.
func pairingReason(err error) string {
	switch {
	case errors.Is(err, libgm.ErrIncorrectEmoji):
		return "wrongEmoji"
	case errors.Is(err, libgm.ErrPairingCancelled):
		return "cancelled"
	case errors.Is(err, libgm.ErrPairingTimeout), errors.Is(err, context.DeadlineExceeded):
		return "timeout"
	case errors.Is(err, libgm.ErrNoDevicesFound):
		return "noDevices"
	case errors.Is(err, libgm.ErrPairingInitTimeout):
		return "phoneNotResponding"
	case errors.Is(err, events.ErrInvalidCredentials):
		return "signedOut"
	default:
		return "other"
	}
}

// Connect starts listening on an already-paired session.
func (b *Bridge) Connect() error {
	if err := b.client.Connect(); err != nil {
		return fmt.Errorf("connecting gmbridge: %w", err)
	}
	return nil
}

// Disconnect stops the client; the session stays valid for reuse.
func (b *Bridge) Disconnect() {
	b.CancelPairing()
	b.client.Disconnect()
}

// IsConnected reports whether the long-poll is up.
func (b *Bridge) IsConnected() bool {
	return b.client.IsConnected()
}

// TakeOver makes this the active Messages-for-Web session again after another
// browser took it (Messages lets one web session be active at a time).
func (b *Bridge) TakeOver() error {
	if err := b.client.SetActiveSession(); err != nil {
		return fmt.Errorf("taking the active session back: %w", err)
	}
	return nil
}

// ExportSession returns the session blob to persist (encrypted at rest by the C# side).
func (b *Bridge) ExportSession() (string, error) {
	blob, err := json.Marshal(b.client.AuthData)
	if err != nil {
		return "", fmt.Errorf("exporting gmbridge session: %w", err)
	}
	return string(blob), nil
}

func (b *Bridge) sendRequest(conversationID string) *gmproto.SendMessageRequest {
	b.lock.Lock()
	participantID := b.outgoingIDs[conversationID]
	sim := b.sims[participantID]
	b.lock.Unlock()
	tmpID := uuid.NewString()
	return &gmproto.SendMessageRequest{
		ConversationID: conversationID,
		MessagePayload: &gmproto.MessagePayload{
			TmpID:          tmpID,
			ConversationID: conversationID,
			ParticipantID:  participantID,
			TmpID2:         tmpID,
		},
		SIMPayload: sim.GetSIMData().GetSIMPayload(),
		TmpID:      tmpID,
	}
}

// SendText sends a plain text message to a conversation. Messages chooses RCS or SMS
// exactly as it would for a message typed into it.
func (b *Bridge) SendText(conversationID string, text string) error {
	req := b.sendRequest(conversationID)
	req.MessagePayload.MessagePayloadContent = &gmproto.MessagePayloadContent{
		MessageContent: &gmproto.MessageContent{Content: text},
	}
	resp, err := b.client.SendMessage(req)
	if err != nil {
		return fmt.Errorf("sending to conversation %s: %w", conversationID, err)
	}
	if resp.GetStatus() != gmproto.SendMessageResponse_SUCCESS {
		return fmt.Errorf("sending to conversation %s: Messages answered %s", conversationID, resp.GetStatus())
	}
	return nil
}

// SendMedia sends an image (or other file) to a conversation: it uploads the
// bytes, then sends a message carrying the uploaded media.
func (b *Bridge) SendMedia(conversationID string, data []byte, fileName string, mime string) error {
	media, err := b.client.UploadMedia(data, fileName, mime)
	if err != nil {
		return fmt.Errorf("uploading media: %w", err)
	}
	req := b.sendRequest(conversationID)
	req.MessagePayload.MessageInfo = []*gmproto.MessageInfo{{
		Data: &gmproto.MessageInfo_MediaContent{MediaContent: media},
	}}
	resp, err := b.client.SendMessage(req)
	if err != nil {
		return fmt.Errorf("sending media to conversation %s: %w", conversationID, err)
	}
	if resp.GetStatus() != gmproto.SendMessageResponse_SUCCESS {
		return fmt.Errorf("sending media to conversation %s: Messages answered %s", conversationID, resp.GetStatus())
	}
	return nil
}

// StartConversation finds or creates the conversation with these numbers (a JSON
// array of strings; more than one makes a group, named groupName when given) and
// returns its conversation JSON — the same shape as a "conversation" event.
func (b *Bridge) StartConversation(numbersJson string, groupName string) (string, error) {
	var numbers []string
	if err := json.Unmarshal([]byte(numbersJson), &numbers); err != nil {
		return "", fmt.Errorf("parsing the numbers to start a conversation with: %w", err)
	}
	if len(numbers) == 0 {
		return "", errors.New("starting a conversation: no numbers given")
	}
	req := &gmproto.GetOrCreateConversationRequest{Numbers: make([]*gmproto.ContactNumber, len(numbers))}
	for i, number := range numbers {
		// MysteriousInt 2 is what both mautrix and Messages for Web send for a phone number.
		req.Numbers[i] = &gmproto.ContactNumber{MysteriousInt: 2, Number: number, Number2: number}
	}
	if len(numbers) > 1 && groupName != "" {
		req.RCSGroupName = &groupName
	}
	resp, err := b.client.GetOrCreateConversation(req)
	if err != nil {
		return "", fmt.Errorf("starting a conversation with %d number(s): %w", len(numbers), err)
	}
	conversation := resp.GetConversation()
	if conversation.GetConversationID() == "" {
		return "", fmt.Errorf("starting a conversation with %d number(s): Messages answered %s with no conversation", len(numbers), resp.GetStatus())
	}
	b.remember(conversation)
	return marshal(conversationDocument(conversation))
}

// DeleteMessage deletes a message in Google Messages (and so on the phone).
func (b *Bridge) DeleteMessage(messageID string) error {
	resp, err := b.client.DeleteMessage(messageID)
	if err != nil {
		return fmt.Errorf("deleting message %s: %w", messageID, err)
	}
	if !resp.GetSuccess() {
		return fmt.Errorf("deleting message %s: Messages reported failure", messageID)
	}
	return nil
}

// MarkRead marks a conversation read up to and including messageID.
func (b *Bridge) MarkRead(conversationID string, messageID string) error {
	if err := b.client.MarkRead(conversationID, messageID); err != nil {
		return fmt.Errorf("marking conversation %s read: %w", conversationID, err)
	}
	return nil
}

// ListConversations returns the most recent inbox conversations as a JSON array
// of conversation documents.
func (b *Bridge) ListConversations(count int) (string, error) {
	resp, err := b.client.ListConversations(count, gmproto.ListConversationsRequest_INBOX)
	if err != nil {
		return "", fmt.Errorf("listing conversations: %w", err)
	}
	out := make([]map[string]any, 0, len(resp.GetConversations()))
	for _, c := range resp.GetConversations() {
		b.remember(c)
		out = append(out, conversationDocument(c))
	}
	return marshal(out)
}

// FetchMessages returns up to count of a conversation's most recent messages as a
// JSON array of message documents (newest first, as Messages returns them).
func (b *Bridge) FetchMessages(conversationID string, count int64) (string, error) {
	resp, err := b.client.FetchMessages(conversationID, count, nil)
	if err != nil {
		return "", fmt.Errorf("fetching messages of conversation %s: %w", conversationID, err)
	}
	out := make([]map[string]any, 0, len(resp.GetMessages()))
	for _, m := range resp.GetMessages() {
		out = append(out, b.messageDocument(m, true))
	}
	return marshal(out)
}

// DownloadMedia fetches and decrypts one attachment, given the media id and the
// base64 decryption key a message document carries.
func (b *Bridge) DownloadMedia(mediaID string, keyBase64 string) ([]byte, error) {
	key, err := base64.StdEncoding.DecodeString(keyBase64)
	if err != nil {
		return nil, fmt.Errorf("reading the decryption key of media %s: %w", mediaID, err)
	}
	data, err := b.client.DownloadMedia(mediaID, key)
	if err != nil {
		return nil, fmt.Errorf("downloading media %s: %w", mediaID, err)
	}
	return data, nil
}

func (b *Bridge) remember(c *gmproto.Conversation) {
	if c.GetConversationID() == "" {
		return
	}
	b.lock.Lock()
	defer b.lock.Unlock()
	if c.GetDefaultOutgoingID() != "" {
		b.outgoingIDs[c.GetConversationID()] = c.GetDefaultOutgoingID()
	}
	for _, p := range c.GetParticipants() {
		if p.GetIsMe() && p.GetID().GetParticipantID() != "" {
			b.selfIDs[p.GetID().GetParticipantID()] = true
		}
	}
}

func (b *Bridge) setSIMs(settings *gmproto.Settings) {
	b.lock.Lock()
	defer b.lock.Unlock()
	for _, sim := range settings.GetSIMCards() {
		id := sim.GetSIMParticipant().GetID()
		if id == "" {
			continue
		}
		b.sims[id] = sim
		b.selfIDs[id] = true
	}
}

func (b *Bridge) isSelf(participantID string) bool {
	b.lock.Lock()
	defer b.lock.Unlock()
	return participantID == "1" || b.selfIDs[participantID]
}

func conversationDocument(c *gmproto.Conversation) map[string]any {
	participants := make([]map[string]any, 0, len(c.GetParticipants()))
	for _, p := range c.GetParticipants() {
		participants = append(participants, map[string]any{
			"id":              p.GetID().GetParticipantID(),
			"number":          p.GetID().GetNumber(),
			"formattedNumber": p.GetFormattedNumber(),
			"name":            p.GetFullName(),
			"isMe":            p.GetIsMe(),
		})
	}
	return map[string]any{
		"type":           "conversation",
		"conversationId": c.GetConversationID(),
		"name":           c.GetName(),
		"isGroup":        c.GetIsGroupChat(),
		"isRcs":          c.GetType() == gmproto.ConversationType_RCS,
		"unread":         c.GetUnread(),
		"timestamp":      c.GetLastMessageTimestamp(),
		"participants":   participants,
	}
}

func (b *Bridge) messageDocument(m *gmproto.Message, isOld bool) map[string]any {
	status := m.GetMessageStatus().GetStatus()
	// Messages numbers every outgoing status below 100 and every incoming one from 100.
	fromMe := (status > 0 && status < 100) || b.isSelf(m.GetParticipantID())
	var text strings.Builder
	media := make([]map[string]any, 0)
	for _, info := range m.GetMessageInfo() {
		if content := info.GetMessageContent(); content != nil {
			text.WriteString(content.GetContent())
		}
		if mc := info.GetMediaContent(); mc != nil {
			media = append(media, map[string]any{
				"mediaId": mc.GetMediaID(),
				"key":     base64.StdEncoding.EncodeToString(mc.GetDecryptionKey()),
				"mime":    mc.GetMimeType(),
				"name":    mc.GetMediaName(),
				"size":    mc.GetSize(),
			})
		}
	}
	return map[string]any{
		"type":           "message",
		"messageId":      m.GetMessageID(),
		"conversationId": m.GetConversationID(),
		"participantId":  m.GetParticipantID(),
		"fromMe":         fromMe,
		"status":         status.String(),
		"failed":         status == gmproto.MessageStatusType_OUTGOING_FAILED_GENERIC || status == gmproto.MessageStatusType_OUTGOING_FAILED_EMERGENCY_NUMBER,
		"text":           text.String(),
		"timestamp":      m.GetTimestamp(),
		"tmpId":          m.GetTmpID(),
		"isOld":          isOld,
		"media":          media,
	}
}

func marshal(value any) (string, error) {
	blob, err := json.Marshal(value)
	if err != nil {
		return "", fmt.Errorf("encoding bridge JSON: %w", err)
	}
	return string(blob), nil
}

func (b *Bridge) emit(event map[string]any) {
	blob, err := json.Marshal(event)
	if err != nil {
		return
	}
	b.handler.OnEvent(string(blob))
}

func (b *Bridge) emitSession() {
	if blob, err := json.Marshal(b.client.AuthData); err == nil {
		b.emit(map[string]any{"type": "sessionChanged", "session": string(blob)})
	}
}

func connection(state string, err error) map[string]any {
	event := map[string]any{"type": "connection", "state": state}
	if err != nil {
		event["error"] = err.Error()
	}
	return event
}

// handleEvent maps libgm's event stream onto the JSON documents Post consumes,
// following mautrix-gmessages' own handler (pkg/connector/handlegmessages.go) for
// which events mean the session is gone.
func (b *Bridge) handleEvent(evt any) {
	switch e := evt.(type) {
	case *libgm.WrappedMessage:
		b.emit(b.messageDocument(e.Message, e.IsOld))
	case *gmproto.Conversation:
		b.remember(e)
		b.emit(conversationDocument(e))
	case *gmproto.Settings:
		b.setSIMs(e)
	case *events.ClientReady:
		for _, c := range e.Conversations {
			b.remember(c)
		}
		b.emit(connection("ready", nil))
	case *events.AuthTokenRefreshed:
		// The tokens in the session moved on; a session saved before this will not sign in.
		b.emitSession()
	case *events.ListenFatalError:
		if errors.Is(e.Error, events.ErrInvalidCredentials) || e.Error.Error() == "http 401 while polling" {
			b.emit(map[string]any{"type": "loggedOut", "reason": "credentials refused: " + e.Error.Error()})
		} else {
			b.emit(connection("fatal", e.Error))
		}
	case *events.ListenTemporaryError:
		b.emit(connection("listenError", e.Error))
	case *events.ListenRecovered:
		b.emit(connection("recovered", nil))
	case *events.PhoneNotResponding:
		b.emit(connection("phoneNotResponding", nil))
	case *events.PhoneRespondingAgain:
		b.emit(connection("phoneResponding", nil))
	case *events.PingFailed:
		if errors.Is(e.Error, events.ErrRequestedEntityNotFound) {
			b.emit(map[string]any{"type": "loggedOut", "reason": "the pairing no longer exists"})
		} else {
			b.emit(connection("listenError", e.Error))
		}
	case *gmproto.RevokePairData:
		b.emit(map[string]any{"type": "loggedOut", "reason": "Google Messages unpaired this device"})
	case *events.GaiaLoggedOut:
		b.emit(map[string]any{"type": "loggedOut", "reason": "the Google account signed out"})
	case *gmproto.UserAlertEvent:
		switch e.GetAlertType() {
		case gmproto.AlertType_BROWSER_INACTIVE:
			b.emit(connection("inactive", nil))
		case gmproto.AlertType_BROWSER_ACTIVE:
			b.emit(connection("active", nil))
		}
	default:
		b.emit(map[string]any{"type": "log", "message": fmt.Sprintf("%T", evt)})
	}
}
