export function getUserInitials(username: string): string {
  const words = username.trim().split(/\s+/);
  if (words.length >= 2) {
    const first = words[0]?.charAt(0) ?? '';
    const last = words[words.length - 1]?.charAt(0) ?? '';
    return (first + last).toUpperCase();
  }
  return username.trim().slice(0, 2).toUpperCase();
}
