import { useState } from 'react';
import type { NotificationPrefs } from './profileTypes';

const NOTIF_KEY = 'cortexa.notifications';

const DEFAULT: NotificationPrefs = { batchCompletion: true, newOpportunities: true };

function load(): NotificationPrefs {
  try {
    const raw = localStorage.getItem(NOTIF_KEY);
    if (!raw) return DEFAULT;
    const parsed = JSON.parse(raw) as Partial<NotificationPrefs>;
    return {
      batchCompletion: typeof parsed.batchCompletion === 'boolean' ? parsed.batchCompletion : true,
      newOpportunities: typeof parsed.newOpportunities === 'boolean' ? parsed.newOpportunities : true,
    };
  } catch {
    return DEFAULT;
  }
}

export function usePreferences() {
  const [notifications, setNotifications] = useState<NotificationPrefs>(load);

  function setNotification(key: keyof NotificationPrefs, value: boolean) {
    const next = { ...notifications, [key]: value };
    setNotifications(next);
    try {
      localStorage.setItem(NOTIF_KEY, JSON.stringify(next));
    } catch {
      /* storage may be unavailable — preference still applies for the session */
    }
  }

  return { notifications, setNotification };
}
