// جسر مؤقت بين الـ API والواجهة: الخادم يخزّن مفتاح صورة نصياً (imageUrl مثل
// "headphones") ريثما يُبنى رفع الصور الحقيقي في المرحلة 3. نحوّل المفتاح لرمز
// تعبيري للعرض، مع بديل عام لأي منتج جديد لا نعرف مفتاحه.
const EMOJI_BY_KEY = {
  headphones: '🎧',
  watch: '⌚',
  keyboard: '⌨️',
  backpack: '🎒',
  sunglasses: '🕶️',
  lamp: '💡',
  coffeepot: '☕',
  mug: '🥤',
};

export const emojiFor = (product) => EMOJI_BY_KEY[product.imageUrl] || '🛍️';
