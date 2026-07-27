const databaseName = "agentic-chat";
const databaseVersion = 1;
const conversationStore = "conversations";

function openDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, databaseVersion);
        request.onupgradeneeded = () => {
            const database = request.result;
            if (!database.objectStoreNames.contains(conversationStore)) {
                const store = database.createObjectStore(conversationStore, { keyPath: "id" });
                store.createIndex("updatedAt", "updatedAt");
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function execute(mode, operation) {
    return openDatabase().then(database => new Promise((resolve, reject) => {
        const transaction = database.transaction(conversationStore, mode);
        const store = transaction.objectStore(conversationStore);
        let request;
        try {
            request = operation(store);
        } catch (error) {
            database.close();
            reject(error);
            return;
        }
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        transaction.oncomplete = () => database.close();
        transaction.onerror = () => {
            database.close();
            reject(transaction.error);
        };
    }));
}

export function list() {
    return execute("readonly", store => store.getAll());
}

export function get(id) {
    return execute("readonly", store => store.get(id));
}

export function put(conversation) {
    return execute("readwrite", store => store.put(conversation));
}

export function remove(id) {
    return execute("readwrite", store => store.delete(id));
}
