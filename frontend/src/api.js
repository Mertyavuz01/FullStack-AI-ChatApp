const API_URL = "http://localhost:5145"; // Backend adresin

// Kullanıcı ekleme
export async function addUser(nickname) {
  const response = await fetch(`${API_URL}/users`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ nickname }), // ✅ id:0 KALDIRILDI
  });

  if (!response.ok) {
    const text = await response.text();
    console.error("addUser hata:", response.status, text);
    throw new Error("Kullanıcı oluşturulamadı");
  }

  const data = await response.json();
  console.log("addUser OK:", data);
  return data;
}

// Mesaj gönderme
export async function sendMessage(userId, text) {
  const response = await fetch(`${API_URL}/messages`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId, text }),
  });

  if (!response.ok) {
    const text = await response.text();
    console.error("sendMessage hata:", response.status, text);
    throw new Error("Mesaj gönderilemedi");
  }

  const data = await response.json();
  console.log("sendMessage OK:", data);
  return data;
}

// Mesajları çekme
export async function getMessages() {
  const response = await fetch(`${API_URL}/messages`);
  return response.json();
}
