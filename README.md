# Ödev Takip (Homework Tracker) 📚

Bu proje, öğrencilerin farklı platformlardan gelen ödevlerini, bildirimlerini ve ders programlarını tek bir merkezde toplayıp, Yapay Zeka (AI) destekli, düzenli bir çalışma planı oluşturmayı amaçlayan bir otomasyon aracıdır.

Özellikle velilerin, çocuklarının ödevlerini takip etmelerini kolaylaştırmak amacıyla geliştirilmiştir. 

*Proje hemen hemen tamamen AI desteği ile oluşturulmuştur. *



## 🎯 Projenin Amacı

Günümüzde öğrencilerin ödevleri ve sorumlulukları birden fazla kaynaktan (okul sistemleri, dershane WhatsApp grupları vb.) gelmektedir. Bu durum takip zorluğuna ve gözden kaçan ödevlere sebep olabilir. 

Ödev Takip uygulaması; bu dağınıklığı ortadan kaldırmak, tüm ödev verilerini otomatik olarak okuyup anlamlandırmak ve öğrenciye/veliye "Bugün ne yapmalıyım?" sorusunun cevabını veren derli toplu bir HTML planı sunmak için geliştirilmiştir.

## 🚀 Neler İçin Kullanılabilir?

* **Çoklu Kaynaktan Veri Toplama:** Çeşitli kanallardan gelen karmaşık ödev mesajlarını tek bir düzene sokmak.
* **Akıllı Çalışma Planı Oluşturma:** Üretken Yapay Zeka (Gemini, OpenAI vb.) kullanarak toplanan ödevleri derslere, teslim tarihlerine ve önceliklerine göre kategorize etmek.
* **Görsel Ders Programı Analizi:** Yüklenen ders programı fotoğraflarını yapay zeka ile analiz edip güncel programa göre ödev eşleştirmesi yapmak.
* **Arşivleme:** Geçmişe dönük ödev belgelerini (PDF, Word, fotoğraf vb.) gruplara göre düzenli bir klasör yapısında saklamak.

## ⚙️ Temel Özellikler ve Entegrasyonlar

Uygulama şu an için aşağıdaki sistemlerle entegre çalışmaktadır:

* **WhatsApp Entegrasyonu:** Belirlenen WhatsApp gruplarına bağlanır, geçmiş mesajları tarar, ödev içeriklerini ve paylaşılan dökümanları (resim, PDF vb.) otomatik olarak indirir.
* **E12 (Öğrenci Bilgi Sistemi) Entegrasyonu:** E12 hesaplarına otomatik giriş yaparak sistemdeki aktif ödevleri ve duyuruları çeker.
* **Genişletilebilir Mimari:** Proje, modüler bir yapıda tasarlanmıştır. İhtiyaç duyulması halinde ilerleyen zamanlarda **Google Classroom, K12NET, Moodle** gibi diğer sistemler de kolayca entegre edilebilir.
* **Yapay Zeka (AI) Desteği:** Toplanan tüm verileri (WhatsApp mesajları + E12 verileri) birleştirip, belirlediğiniz AI sağlayıcısı (Gemini veya OpenAI) aracılığıyla şık ve anlaşılır bir HTML raporuna dönüştürür.

## 🛠 Nasıl Çalışır?

1. **Ayarlar:** Uygulama üzerinden WhatsApp gruplarınızı, E12 hesap bilgilerinizi ve AI (API Key) ayarlarınızı tanımlayın.
2. **Ders Programı:** İlgili kurumlar (okul, dershane) için ders programı görsellerini sisteme yükleyin ve AI ile analiz ettirin.
3. **Veri Toplama:** "Tüm Ödevleri Çıkart" veya tekil butonlar ile kaynaklardan ödevleri çekin. Bot sizin yerinize işlemleri halleder.
4. **Plan Oluşturma:** "AI Plan Oluştur" butonu ile toplanan tüm verilerden nihai, düzenli çalışma planınızı (HTML) oluşturun.
