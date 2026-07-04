
import { writable } from 'svelte/store';

function persistStore(key: string, defaultValue: string | null) {
    const storedValue = localStorage?.getItem(key);
    let initialValue: string = defaultValue as string;
    if (storedValue) {
        try {
            initialValue = JSON.parse(storedValue);
        } catch {
            localStorage?.removeItem(key);
        }
    }

    const store = writable(initialValue);

    store.subscribe((value) => {
        localStorage?.setItem(key, JSON.stringify(value));
    });

    return store;
}

function generateGUID() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        const r = (Math.random() * 16) | 0,
            v = c === 'x' ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}

export const clientIdentifierStore = persistStore('clientIdentifier', generateGUID());
export const authSecretStore = persistStore('authSecret', null);