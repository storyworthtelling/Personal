// Package gmbridge is a deliberately tiny, gomobile-friendly shim over libgm
// (the reverse-engineered Google Messages-for-Web client maintained for
// mautrix-gmessages). The Communication app embeds the resulting .aar via a
// .NET binding project; this surface is kept to primitives and one callback
// interface so the C# side never touches Go semantics.
//
// Build (requires Android NDK + gomobile; see build.sh):
//
//	./build.sh
//
// NOTE: written against libgm as vendored by go.mau.fi/mautrix-gmessages v0.6.4.
// libgm's API tracks Google's protocol changes; pin bumps in go.mod may require
// small adjustments here — that is the point of this shim: churn stays here.
package gmbridge

import (
	"context"
	"encoding/json"
	"fmt"
	"strings"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"go.mau.fi/mautrix-gmessages/pkg/libgm"
	"go.mau.fi/mautrix-gmessages/pkg/libgm/events"
	"go.mau.fi/mautrix-gmessages/pkg/libgm/gmproto"
)

// EventHandler receives every bridge event as a JSON document:
//
//	{"type":"message","conversationId":"...","sender":"...","text":"...","timestampMs":123}
//	{"type":"paired"} {"type":"authError","error":"..."} {"type":"log","message":"..."}
type EventHandler interface {
	OnEvent(eventJson string)
}

// Bridge wraps one libgm client session.
type Bridge struct {
	client  *libgm.Client
	handler EventHandler
	cancel  context.CancelFunc
}

// NewBridge creates a bridge. sessionJson is the blob previously returned by
// ExportSession ("" for a fresh, unpaired session).
func NewBridge(sessionJson string, handler EventHandler) (*Bridge, error) {
	authData := libgm.NewAuthData()
	if sessionJson != "" {
		if err := json.Unmarshal([]byte(sessionJson), authData); err != nil {
			return nil, fmt.Errorf("restoring gmbridge session: %w", err)
		}
	}

	b := &Bridge{handler: handler}
	// libgm v0.6.4: NewClient takes (authData, pushKeys, logger); the event
	// handler is registered separately.
	b.client = libgm.NewClient(authData, nil, zerolog.Nop())
	b.client.SetEventHandler(b.handleEvent)
	return b, nil
}

// StartGoogleAccountPairing begins Google-account pairing (the only mode that
// works when the client runs on the same phone as Google Messages; QR pairing
// requires a second screen). Returns the emoji the user must confirm in the
// Messages app; pairing then finishes asynchronously and emits {"type":"paired"}
// (or {"type":"authError"}).
func (b *Bridge) StartGoogleAccountPairing(oauthCookiesJson string) (string, error) {
	var cookies map[string]string
	if err := json.Unmarshal([]byte(oauthCookiesJson), &cookies); err != nil {
		return "", fmt.Errorf("parsing pairing cookies: %w", err)
	}
	b.client.AuthData.SetCookies(cookies)

	emoji, pairingSession, err := b.client.StartGaiaPairing(context.Background())
	if err != nil {
		return "", fmt.Errorf("starting Google-account pairing: %w", err)
	}

	// FinishGaiaPairing blocks until the user confirms the emoji in Google
	// Messages, so run it off the calling thread and report the outcome as an event.
	go func() {
		if _, err := b.client.FinishGaiaPairing(context.Background(), pairingSession); err != nil {
			b.emit(map[string]any{"type": "authError", "error": err.Error()})
			return
		}
		b.emit(map[string]any{"type": "paired"})
	}()
	return emoji, nil
}

// Connect starts listening for events on an already-paired session.
func (b *Bridge) Connect() error {
	if err := b.client.Connect(); err != nil {
		return fmt.Errorf("connecting gmbridge: %w", err)
	}
	return nil
}

// Disconnect stops the client; the session blob stays valid for reuse.
func (b *Bridge) Disconnect() {
	if b.cancel != nil {
		b.cancel()
	}
	b.client.Disconnect()
}

// ExportSession returns the session blob to persist (encrypted at rest by the
// C# side).
func (b *Bridge) ExportSession() (string, error) {
	blob, err := json.Marshal(b.client.AuthData)
	if err != nil {
		return "", fmt.Errorf("exporting gmbridge session: %w", err)
	}
	return string(blob), nil
}

// SendText sends a plain text message to a conversation.
func (b *Bridge) SendText(conversationID string, text string) error {
	_, err := b.client.SendMessage(&gmproto.SendMessageRequest{
		ConversationID: conversationID,
		MessagePayload: &gmproto.MessagePayload{
			TmpID: uuid.NewString(),
			MessagePayloadContent: &gmproto.MessagePayloadContent{
				MessageContent: &gmproto.MessageContent{Content: text},
			},
		},
		TmpID: uuid.NewString(),
	})
	if err != nil {
		return fmt.Errorf("sending to conversation %s: %w", conversationID, err)
	}
	return nil
}

// ListConversations returns the most recent conversations as a JSON array of
// {"id":"...","title":"...","lastMessage":"...","timestampMs":123}.
func (b *Bridge) ListConversations(count int) (string, error) {
	resp, err := b.client.ListConversations(count, gmproto.ListConversationsRequest_INBOX)
	if err != nil {
		return "", fmt.Errorf("listing conversations: %w", err)
	}

	type conversation struct {
		Id          string `json:"id"`
		Title       string `json:"title"`
		LastMessage string `json:"lastMessage"`
		TimestampMs int64  `json:"timestampMs"`
	}
	out := make([]conversation, 0, len(resp.GetConversations()))
	for _, c := range resp.GetConversations() {
		out = append(out, conversation{
			Id:          c.GetConversationID(),
			Title:       c.GetName(),
			LastMessage: c.GetLatestMessage().GetDisplayContent(),
			TimestampMs: c.GetLastMessageTimestamp(),
		})
	}
	blob, err := json.Marshal(out)
	if err != nil {
		return "", fmt.Errorf("encoding conversations: %w", err)
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

// handleEvent maps the libgm event stream onto the JSON documents the C# side
// consumes. Only incoming messages and pairing success are surfaced; everything
// else is logged.
func (b *Bridge) handleEvent(evt any) {
	switch e := evt.(type) {
	case *libgm.WrappedMessage:
		b.emit(map[string]any{
			"type":           "message",
			"conversationId": e.GetConversationID(),
			"sender":         e.GetParticipantID(),
			"text":           messageText(e),
			"timestampMs":    e.GetTimestamp(),
		})
	case *events.PairSuccessful:
		b.emit(map[string]any{"type": "paired"})
	default:
		b.emit(map[string]any{"type": "log", "message": fmt.Sprintf("%T", evt)})
	}
}

// messageText concatenates the text parts of a message (v0.6.4 splits a message
// body across MessageInfo entries).
func messageText(msg *libgm.WrappedMessage) string {
	var builder strings.Builder
	for _, info := range msg.GetMessageInfo() {
		builder.WriteString(info.GetMessageContent().GetContent())
	}
	return builder.String()
}
