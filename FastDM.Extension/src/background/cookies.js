// ঐচ্ছিক কুকি পাঠানো। ইউজার অপশন পেজে চালু করলে তবেই "cookies" + হোস্ট permission চাওয়া হয়।
// permission না থাকলে এই ফাংশনগুলো চুপচাপ খালি ফেরত দেয়: কুকি ছাড়াই ডাউনলোড চলে।
export const COOKIE_PERMS = Object.freeze({ permissions: ['cookies'], origins: ['<all_urls>'] });

export async function hasCookieAccess() {
  try {
    return await chrome.permissions.contains(COOKIE_PERMS);
  } catch {
    return false;
  }
}

// অপশন পেজের বাটন-ক্লিক (user gesture) থেকে ডাকতে হবে
export async function enableCookieAccess() {
  return chrome.permissions.request(COOKIE_PERMS);
}

export async function disableCookieAccess() {
  return chrome.permissions.remove(COOKIE_PERMS);
}

// এই URL-এর জন্য ব্রাউজারের কুকি → bridge ফরম্যাট। শুধু http/https।
export async function cookiesFor(url) {
  if (!/^https?:/i.test(url || '')) return [];
  if (!(await hasCookieAccess())) return [];
  try {
    const list = await chrome.cookies.getAll({ url });
    return list.map((c) => ({
      name: c.name,
      value: c.value,
      domain: c.domain,
      path: c.path,
      secure: !!c.secure,
    }));
  } catch {
    return [];
  }
}
