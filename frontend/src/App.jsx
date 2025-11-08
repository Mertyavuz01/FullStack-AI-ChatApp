import { useEffect, useState } from "react";
import { addUser, sendMessage, getMessages } from "./api";

function App() {
  const [nickname, setNickname] = useState("");
  const [user, setUser] = useState(null);
  const [text, setText] = useState("");
  const [messages, setMessages] = useState([]);

  // Mesajları yükleme
  useEffect(() => {
    fetchMessages();
  }, []);

  async function fetchMessages() {
    const data = await getMessages();
    setMessages(data);
  }

  // ✅ Kullanıcı ekleme
  const handleAddUser = async () => {
    if (!nickname.trim()) {
      alert("Lütfen bir kullanıcı adı girin!");
      return;
    }

    try {
      const data = await addUser(nickname);
      setUser(data); // Kullanıcı state’e kaydediliyor
      alert(`Hoş geldin, ${data.nickname}!`);
    } catch (error) {
      console.error("Kullanıcı eklenemedi:", error);
      alert("Kullanıcı eklenirken hata oluştu!");
    }
  };

  // ✅ Mesaj gönderme
  const handleSendMessage = async () => {
    if (!user) {
      alert("Önce kullanıcı ekleyin!");
      return;
    }
    if (!text.trim()) {
      alert("Boş mesaj gönderilemez!");
      return;
    }

    try {
      await sendMessage(user.id, text);
      setText("");
      fetchMessages();
    } catch (error) {
      console.error("Mesaj gönderilemedi:", error);
      alert("Mesaj gönderilirken hata oluştu!");
    }
  };

  return (
    <div style={{ padding: "40px", fontFamily: "Arial", color: "white", background: "#1c1c1c", minHeight: "100vh" }}>
      <h1>
        💬 <b>Chat Uygulaması</b>
      </h1>

      <div style={{ marginTop: "30px" }}>
        <h3>Kullanıcı Ekle</h3>
        <input
          type="text"
          placeholder="Kullanıcı adı..."
          value={nickname}
          onChange={(e) => setNickname(e.target.value)}
          style={{ padding: "10px", width: "250px", marginRight: "10px", borderRadius: "6px", border: "1px solid #444" }}
        />
        <button
          onClick={handleAddUser}
          style={{ padding: "10px 20px", background: "#7b5cff", color: "white", borderRadius: "6px", border: "none" }}
        >
          Ekle
        </button>
      </div>

      <div style={{ marginTop: "30px" }}>
        <h3>Mesaj Gönder</h3>
        <input
          type="text"
          placeholder="Mesaj yaz..."
          value={text}
          onChange={(e) => setText(e.target.value)}
          style={{ padding: "10px", width: "400px", marginRight: "10px", borderRadius: "6px", border: "1px solid #444" }}
        />
        <button
          onClick={handleSendMessage}
          style={{ padding: "10px 20px", background: "#7b5cff", color: "white", borderRadius: "6px", border: "none" }}
        >
          Gönder
        </button>
      </div>

      <div style={{ marginTop: "40px" }}>
        <h3>Mesajlar:</h3>
        {messages.length === 0 ? (
          <p>Henüz mesaj yok.</p>
        ) : (
          messages.map((msg) => (
            <div
              key={msg.id}
              style={{
                border: "1px solid #444",
                borderRadius: "8px",
                padding: "10px",
                marginTop: "10px",
                backgroundColor: "#2a2a2a",
              }}
            >
              <p>
                <b>{msg.user?.nickname}</b>: {msg.text}
              </p>
              <p style={{ fontSize: "14px", color: "#bbb" }}>💭 Duygu: {msg.sentiment}</p>
            </div>
          ))
        )}
      </div>
    </div>
  );
}

export default App;




